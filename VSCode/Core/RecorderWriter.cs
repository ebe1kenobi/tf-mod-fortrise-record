using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
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
    public MSColor[] Pixels;   // null si l'image n'est pas enregistree
    public int Width;
    public int Height;
    public string InputsLine;  // ligne JSON (inputs.jsonl) ou null
    public string StateLine;   // ligne JSON (state.jsonl) ou null
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
    private readonly Thread thread;
    private StreamWriter inputsFile;
    private StreamWriter stateFile;
    public long Dropped { get; private set; }

    public RecorderWriter(string dir)
    {
      this.dir = dir;
      thread = new Thread(Run) { IsBackground = true, Name = "TFRecordWriter" };
      thread.Start();
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

    // Encodage PNG purement CPU (System.Drawing) : ne necessite pas le
    // GraphicsDevice, donc peut tourner sur ce thread de fond.
    // XNA Color est en RGBA ; Bitmap Format32bppArgb est en BGRA en memoire.
    private void SavePng(FrameJob job)
    {
      int w = job.Width, h = job.Height;
      MSColor[] px = job.Pixels;
      byte[] bgra = new byte[w * h * 4];
      for (int i = 0; i < px.Length; i++)
      {
        int o = i * 4;
        bgra[o] = px[i].B;
        bgra[o + 1] = px[i].G;
        bgra[o + 2] = px[i].R;
        bgra[o + 3] = px[i].A;
      }

      using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
      {
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(bgra, 0, data.Scan0, bgra.Length); }
        finally { bmp.UnlockBits(data); }
        string path = Path.Combine(dir, "frame_" + job.FrameIndex.ToString("D6") + ".png");
        bmp.Save(path, ImageFormat.Png);
      }
    }
  }
}
