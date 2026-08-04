using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace TFModFortRiseRecord
{
  // Point d'entree de l'export GIF, volontairement separe de GifEncoderCore : ici
  // aucun type d'ImageSharp n'apparait, donc le JIT ne tente de resoudre l'assembly
  // qu'en entrant dans GifEncoderCore.Encode. L'appel est enveloppe pour qu'une
  // DLL manquante ou incompatible desactive l'export au lieu de tuer le thread —
  // c'est ce mecanisme qui manquait a SavePng au tout debut.
  //
  // L'encodage tourne sur SON PROPRE thread, pas sur celui de RecorderWriter :
  // un round de 277 images demande une quinzaine de secondes, pendant lesquelles
  // le round suivant continue d'etre capture. Bloquer le thread d'ecriture
  // remplirait sa file (240 frames) et ferait dropper des images.
  internal static class GifExport
  {
    // round_00_frame_000123.png -> round 00, frame 123
    private static readonly Regex FrameRe =
        new Regex(@"^round_(\d+)_frame_(\d+)\.png$", RegexOptions.IgnoreCase);

    private sealed class Request
    {
      public string Dir;
      public int Round, Fps, Every, Colors;
    }

    private static readonly BlockingCollection<Request> queue =
        new BlockingCollection<Request>();
    private static readonly object startLock = new object();
    private static Thread thread;
    private static bool unavailable;

    // Appele depuis le thread d'ecriture, une fois tous les PNG du round poses sur
    // le disque. Ne bloque pas : l'encodage est simplement mis en file.
    public static void Post(string dir, int round, int fps, int every, int colors)
    {
      if (unavailable) return;

      EnsureThread();
      queue.Add(new Request
      {
        Dir = dir,
        Round = round,
        Fps = fps,
        Every = every,
        Colors = colors,
      });
    }

    private static void EnsureThread()
    {
      if (thread != null) return;
      lock (startLock)
      {
        if (thread != null) return;
        thread = new Thread(Run) { IsBackground = true, Name = "TFRecordGif" };
        thread.Start();
      }
    }

    private static void Run()
    {
      foreach (Request req in queue.GetConsumingEnumerable())
      {
        if (unavailable) continue;
        try { Encode(req); }
        catch (Exception e)
        {
          // Typiquement FileNotFoundException/TypeLoadException si la DLL
          // SixLabors.ImageSharp n'a pas ete deployee a cote du mod.
          unavailable = true;
          Logger.Error("Export GIF desactive pour cette session : " + e);
        }
      }
    }

    private static void Encode(Request req)
    {
      // Les PNG sont relus depuis le disque : rien n'est garde en memoire, la
      // ou un round de 277 images pese 85 Mo en RGBA.
      List<string> frames = CollectFrames(req.Dir, req.Round);
      if (frames.Count == 0) return;

      // Les GIF sont regroupes a cote des dossiers de match, comme le fait
      // tools/make_gif.py, et prefixes du nom du match.
      string matchName = new DirectoryInfo(req.Dir).Name;
      string gifDir = Path.Combine(Path.GetDirectoryName(req.Dir), "gif");
      string output = Path.Combine(gifDir,
          matchName + "_round_" + req.Round.ToString("D2") + ".gif");

      long size = GifEncoderCore.Encode(frames, output, req.Fps, req.Every, req.Colors);
      Logger.Info("GIF round " + req.Round + " : " + frames.Count + " images -> "
          + (size / 1024) + " Ko");
    }

    private static List<string> CollectFrames(string sessionDir, int round)
    {
      List<KeyValuePair<int, string>> found = new List<KeyValuePair<int, string>>();

      foreach (string path in Directory.EnumerateFiles(sessionDir, "*.png"))
      {
        Match m = FrameRe.Match(Path.GetFileName(path));
        if (!m.Success) continue;
        if (int.Parse(m.Groups[1].Value) != round) continue;
        found.Add(new KeyValuePair<int, string>(int.Parse(m.Groups[2].Value), path));
      }

      found.Sort((a, b) => a.Key.CompareTo(b.Key));

      List<string> ordered = new List<string>(found.Count);
      foreach (KeyValuePair<int, string> kv in found)
        ordered.Add(kv.Value);
      return ordered;
    }
  }
}
