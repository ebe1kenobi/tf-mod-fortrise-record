using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using MSColor = Microsoft.Xna.Framework.Color;

namespace TFModFortRiseRecord
{
  // Une frame capturee, prete a etre ecrite hors du thread de rendu.
  // Pixels est un buffer emprunte au pool (rendu apres ecriture).
  internal sealed class FrameJob
  {
    public int FrameIndex;
    public int Round;          // index du round dans le match (0 pour le premier)
    public MSColor[] Pixels;   // null si l'image n'est pas enregistree
    public int Width;
    public int Height;
    public string InputsLine;  // ligne JSON (inputs.jsonl) ou null
    public string StateLine;   // ligne JSON (state.jsonl) ou null

    // >= 0 : marqueur de fin de round. Traite dans l'ordre de la file, donc
    // garanti apres l'ecriture de tous les PNG du round.
    public int ExportGifRound = -1;
  }

  // Thread de fond : consomme les FrameJob et fait tout le travail lourd
  // (encodage PNG, ecriture disque) pour ne PAS ralentir le thread de rendu.
  // Le thread de rendu ne fait qu'une copie memoire (GetData) + un enqueue.
  internal sealed class RecorderWriter
  {
    private const int MaxQueued = 240;      // au-dela, on droppe pour borner la memoire
    private const int PixelCount = 320 * 240;

    private static readonly ConcurrentBag<MSColor[]> BufferPool = new ConcurrentBag<MSColor[]>();

    private readonly BlockingCollection<FrameJob> queue = new BlockingCollection<FrameJob>();
    private readonly string dir;
    private readonly CompressionLevel compression;
    private readonly bool gif;
    private readonly int gifFps;
    private readonly int gifEvery;
    private readonly int gifColors;
    private readonly Thread thread;
    private StreamWriter inputsFile;
    private StreamWriter stateFile;
    public long Dropped { get; private set; }

    // Les reglages sont figes a l'ouverture de la session : le thread de fond n'a
    // ainsi pas a relire les settings pendant qu'ils changent.
    public RecorderWriter(string dir, CompressionLevel compression,
                          bool gif, int gifFps, int gifEvery, int gifColors)
    {
      this.dir = dir;
      this.compression = compression;
      this.gif = gif;
      this.gifFps = gifFps;
      this.gifEvery = gifEvery;
      this.gifColors = gifColors;
      thread = new Thread(Run) { IsBackground = true, Name = "TFRecordWriter" };
      thread.Start();
    }

    // Marque la fin d'un round. L'encodage se fait sur le thread de fond, apres
    // que toutes les frames deja enfilees ont ete ecrites sur le disque.
    public void EnqueueGifExport(int round)
    {
      if (!gif) return;
      Enqueue(new FrameJob { ExportGifRound = round });
    }

    public static MSColor[] RentBuffer()
    {
      MSColor[] buf;
      if (BufferPool.TryTake(out buf)) return buf;
      return new MSColor[PixelCount];
    }

    private static void ReturnBuffer(MSColor[] buf)
    {
      if (buf != null && buf.Length == PixelCount)
        BufferPool.Add(buf);
    }

    // Appele depuis le thread de rendu. Non bloquant : si la file est saturee
    // (disque trop lent), on droppe la frame plutot que de figer le jeu.
    public void Enqueue(FrameJob job)
    {
      if (queue.IsAddingCompleted) return;
      if (queue.Count >= MaxQueued)
      {
        Dropped++;
        ReturnBuffer(job.Pixels);
        return;
      }
      queue.Add(job);
    }

    // Signale la fin ; le thread draine la file puis se termine.
    public void Complete()
    {
      if (!queue.IsAddingCompleted)
        queue.CompleteAdding();
    }

    private void Run()
    {
      try
      {
        foreach (FrameJob job in queue.GetConsumingEnumerable())
        {
          try { WriteJob(job); }
          catch (Exception e) { Logger.Error("RecorderWriter.WriteJob: " + e); }
          finally { ReturnBuffer(job.Pixels); }
        }
      }
      catch (Exception e) { Logger.Error("RecorderWriter.Run: " + e); }
      finally
      {
        try { inputsFile?.Flush(); inputsFile?.Dispose(); } catch { }
        try { stateFile?.Flush(); stateFile?.Dispose(); } catch { }
      }
    }

    private void WriteJob(FrameJob job)
    {
      if (job.ExportGifRound >= 0)
      {
        // Non bloquant : l'encodage part sur son propre thread (voir GifExport).
        GifExport.Post(dir, job.ExportGifRound, gifFps, gifEvery, gifColors);
        return;
      }

      if (job.Pixels != null)
        SavePng(job);

      if (job.InputsLine != null)
      {
        if (inputsFile == null)
          inputsFile = new StreamWriter(Path.Combine(dir, "inputs.jsonl"), true);
        inputsFile.WriteLine(job.InputsLine);
        inputsFile.Flush();
      }

      if (job.StateLine != null)
      {
        if (stateFile == null)
          stateFile = new StreamWriter(Path.Combine(dir, "state.jsonl"), true);
        stateFile.WriteLine(job.StateLine);
        stateFile.Flush();
      }
    }

    // Encodage PNG purement CPU (voir PngEncoder) : ne necessite pas le
    // GraphicsDevice, donc peut tourner sur ce thread de fond.
    // XNA Color est deja en RGBA, l'ordre attendu par PngEncoder.
    private void SavePng(FrameJob job)
    {
      int w = job.Width, h = job.Height;
      MSColor[] px = job.Pixels;
      // Le buffer vient du pool et fait toujours PixelCount : ne lire que la
      // zone reellement couverte par la frame.
      int count = Math.Min(w * h, px.Length);

      // Le RenderTarget compose est opaque en pratique : on ecrit du RGB (un
      // quart de donnees en moins, sans perte) en verifiant l'alpha au passage,
      // et on ne refait une passe en RGBA que si une frame transparente sort.
      // Le ET vaut 255 si et seulement si tous les alphas valent 255.
      byte[] rgb = new byte[w * h * 3];
      int alphaAll = 255;
      for (int i = 0; i < count; i++)
      {
        int o = i * 3;
        rgb[o] = px[i].R;
        rgb[o + 1] = px[i].G;
        rgb[o + 2] = px[i].B;
        alphaAll &= px[i].A;
      }

      string path = Path.Combine(dir, RoundPrefix(job.Round) + "frame_" + job.FrameIndex.ToString("D6") + ".png");

      if (alphaAll == 255)
      {
        PngEncoder.Write(path, w, h, rgb, 3, compression);
        return;
      }

      byte[] rgba = new byte[w * h * 4];
      for (int i = 0; i < count; i++)
      {
        int o = i * 4;
        rgba[o] = px[i].R;
        rgba[o + 1] = px[i].G;
        rgba[o + 2] = px[i].B;
        rgba[o + 3] = px[i].A;
      }
      PngEncoder.Write(path, w, h, rgba, 4, compression);
    }

    // Prefixe commun a tous les fichiers d'un round ("round_00_"), pour que le
    // tri alphabetique du dossier suive l'ordre de jeu.
    public static string RoundPrefix(int round)
    {
      return "round_" + round.ToString("D2") + "_";
    }
  }
}
