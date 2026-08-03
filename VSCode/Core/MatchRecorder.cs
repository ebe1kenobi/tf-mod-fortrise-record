using System;
using System.IO;
using FortRise;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using TowerFall;

namespace TFModFortRiseRecord
{
  // Orchestre l'enregistrement d'un match. Tout le travail lourd (encodage PNG,
  // ecriture disque) est delegue a RecorderWriter sur un thread de fond ; ici on
  // ne fait, sur le thread de rendu, qu'une copie memoire des pixels + la lecture
  // des inputs/positions, puis un enqueue non bloquant.
  //
  // Source des images : Engine.Instance.Screen.RenderTarget, le buffer compose du
  // jeu (le meme que le systeme de replay), capture APRES le rendu (postfix sur
  // Engine.Draw), quand il n'est plus la cible active et peut donc etre lu.
  public class MatchRecorder : IHookable
  {
    private const int Width = 320;
    private const int Height = 240;

    private static bool matchActive;
    private static string sessionDir;
    private static int frameIndex;
    private static double accumulator;
    private static RecorderWriter writer;
    private static object lastLevel;

    public static void Load(IHarmony harmony)
    {
      // Engine.Draw est privee : patch par nom. Postfix pour capturer APRES le rendu.
      harmony.Patch(
          AccessTools.DeclaredMethod(typeof(Monocle.Engine), "Draw"),
          postfix: new HarmonyMethod(Draw_patch)
      );
    }

    private static void Draw_patch(GameTime gameTime)
    {
      try
      {
        Update(gameTime);
      }
      catch (Exception e)
      {
        Logger.Error("MatchRecorder: " + e);
      }
    }

    private static void Update(GameTime gameTime)
    {
      TFModFortRiseRecordSettings settings = SafeSettings();
      if (settings == null || !settings.recordEnabled)
      {
        if (matchActive) StopSession();
        return;
      }

      Scene scene = Engine.Instance != null ? Engine.Instance.Scene : null;

      // Fin de match : retour menu / carte -> on cloture (un match = un dossier).
      if (scene == null || scene is MainMenu || scene is MapScene)
      {
        if (matchActive) StopSession();
        return;
      }

      // Debut de match : on ouvre une session sur la premiere scene de gameplay.
      if (!matchActive)
      {
        if (!(scene is Level)) return;
        StartSession();
      }

      // Throttle commun (images + inputs + etat) au FPS configure.
      int fps = settings.recordFps;
      if (fps < 1) fps = 1;
      double interval = 1.0 / fps;
      accumulator += gameTime.ElapsedGameTime.TotalSeconds;
      if (accumulator < interval) return;
      accumulator -= interval;
      if (accumulator > 1.0) accumulator = 0.0; // anti-derive apres un gros lag

      CaptureFrame(settings, scene);
    }

    private static void CaptureFrame(TFModFortRiseRecordSettings settings, Scene scene)
    {
      Level level = scene as Level;

      // Grille de solides : une seule fois par nouveau niveau (geometrie statique).
      if (settings.recordState && level != null && !ReferenceEquals(level, lastLevel))
      {
        lastLevel = level;
        try
        {
          string gridPath = Path.Combine(sessionDir, "level_" + frameIndex.ToString("D6") + ".json");
          File.WriteAllText(gridPath, StateCapture.BuildLevelGridJson(level));
        }
        catch (Exception e) { Logger.Error("MatchRecorder.grid: " + e); }
      }

      FrameJob job = new FrameJob { FrameIndex = frameIndex, Width = Width, Height = Height };
      bool any = false;

      if (settings.recordImages)
      {
        RenderTarget2D rt = GetScreenTarget();
        if (rt != null)
        {
          MSColorFill(rt, job);
          any = true;
        }
      }

      if (settings.recordInputs)
      {
        job.InputsLine = StateCapture.BuildInputsLine(frameIndex);
        any = true;
      }

      if (settings.recordState && level != null)
      {
        job.StateLine = StateCapture.BuildStateLine(frameIndex, level);
        any = true;
      }

      if (any && writer != null)
      {
        writer.Enqueue(job);
        frameIndex++;
      }
    }

    // Copie rapide GPU->CPU des pixels dans un buffer emprunte au pool.
    private static void MSColorFill(RenderTarget2D rt, FrameJob job)
    {
      var buf = RecorderWriter.RentBuffer();
      rt.GetData<Microsoft.Xna.Framework.Color>(buf);
      job.Pixels = buf;
      job.Width = rt.Width;
      job.Height = rt.Height;
    }

    private static RenderTarget2D GetScreenTarget()
    {
      Screen screen = Engine.Instance != null ? Engine.Instance.Screen : null;
      RenderTarget2D rt = screen != null ? screen.RenderTarget : null;
      if (rt == null || rt.IsDisposed) return null;
      return rt;
    }

    private static TFModFortRiseRecordSettings SafeSettings()
    {
      if (TFModFortRiseRecordModule.Instance == null) return null;
      return TFModFortRiseRecordModule.Settings;
    }

    private static void StartSession()
    {
      string baseDir = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
          "TowerFall", "Recordings");
      sessionDir = Path.Combine(baseDir, "match_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
      Directory.CreateDirectory(sessionDir);
      frameIndex = 0;
      accumulator = 0.0;
      lastLevel = null;
      writer = new RecorderWriter(sessionDir);
      matchActive = true;
      Logger.Info("Recording started -> " + sessionDir);
    }

    private static void StopSession()
    {
      if (writer != null)
      {
        writer.Complete();
        Logger.Info("Recording stopped (" + frameIndex + " frames, " + writer.Dropped + " dropped) -> " + sessionDir);
        writer = null;
      }
      matchActive = false;
      sessionDir = null;
      lastLevel = null;
    }
  }
}
