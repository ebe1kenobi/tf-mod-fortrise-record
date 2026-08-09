using System;
using System.IO;
using System.IO.Compression;
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
    private static bool capturing;
    private static string sessionDir;
    private static int frameIndex;
    private static double accumulator;
    private static RecorderWriter writer;
    private static object lastLevel;
    private static object endedLogic;

    public static void Load(IHarmony harmony)
    {
      // Engine.Draw est privee : patch par nom. Postfix pour capturer APRES le rendu.
      harmony.Patch(
          AccessTools.DeclaredMethod(typeof(Monocle.Engine), "Draw"),
          postfix: new HarmonyMethod(Draw_patch)
      );
      // Fin de round : voir EndRound_prefix.
      harmony.Patch(
          AccessTools.DeclaredMethod(typeof(Session), nameof(Session.EndRound)),
          prefix: new HarmonyMethod(EndRound_prefix)
      );
    }

    // Level.Ending est pose des que le round est DECIDE, c'est-a-dire avant que la
    // mort du dernier joueur ne soit jouee a l'ecran : couper la capture dessus
    // amputait la fin de chaque round. Session.EndRound arrive au terme du
    // RoundEndCounter (90 frames, plus l'attente des fantomes), donc apres la mort,
    // le ralenti et le spotlight, et juste avant que le ReplayViewer ne prenne la
    // main pour le rewind — qu'on ne veut pas enregistrer.
    //
    // On memorise la RoundLogic terminee plutot qu'un simple booleen : elle est
    // recreee a chaque Session.LevelLoadStart, donc le round suivant se distingue
    // tout seul, sans remise a zero a gerer.
    public static void EndRound_prefix(Session __instance)
    {
      endedLogic = __instance != null ? __instance.RoundLogic : null;

      // Toutes les frames du round sont deja dans la file : on y ajoute le
      // marqueur d'export, traite a son tour par le thread de fond.
      if (writer != null && __instance != null)
      {
        try
        {
          int round = __instance.RoundIndex < 0 ? 0 : __instance.RoundIndex;
          writer.EnqueueGifExport(round);
        }
        catch (Exception e) { Logger.Error("MatchRecorder.EndRound: " + e); }
      }
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

      // Entre deux rounds la scene est un LevelLoaderXML : rien a capturer,
      // mais on ne cloture pas la session (un match = plusieurs rounds).
      Level level = scene as Level;
      if (level == null) return;

      bool live = IsRoundLive(level);

      // Debut de match : on ouvre la session au premier round reellement lance,
      // pas des le chargement du niveau (evite de filmer la cinematique FIGHT!).
      if (!matchActive)
      {
        if (!live) return;
        StartSession(settings);
      }

      // Round fige (cinematique de debut ou de fin, pause) : on gele la capture
      // et on repart d'un accumulateur propre pour ne pas capturer en rafale
      // au retour.
      if (live != capturing)
      {
        capturing = live;
        accumulator = 0.0;
        Logger.Info((live ? "Recording resumed" : "Recording suspended")
            + " (round " + RoundOf(level) + ", frame " + frameIndex + ")");
      }
      if (!live) return;

      // Throttle commun (images + inputs + etat) au FPS configure.
      int fps = settings.recordFps;
      if (fps < 1) fps = 1;
      double interval = 1.0 / fps;
      accumulator += gameTime.ElapsedGameTime.TotalSeconds;
      if (accumulator < interval) return;
      accumulator -= interval;
      if (accumulator > 1.0) accumulator = 0.0; // anti-derive apres un gros lag

      CaptureFrame(settings, level);
    }

    // Vrai uniquement quand le round tourne pour de bon.
    //
    // RoundStarted passe a true dans Session.StartRound(), l'appel qui degele
    // aussi les joueurs a la fin de la cinematique FIGHT! ; le RoundLogic est
    // recree a chaque Session.LevelLoadStart, donc le flag retombe seul au round
    // suivant. La borne de fin est posee par EndRound_prefix (voir son commentaire).
    // Level.Paused couvre toutes les entrees en pause (menu Start, hold-to-pause,
    // manette debranchee, perte de focus) : Level.HandlePausing les fait toutes
    // passer par ce setter, et le PauseMenu le remet a false a la reprise.
    private static bool IsRoundLive(Level level)
    {
      if (level.Paused) return false;
      Session session = level.Session;
      if (session == null) return false;
      RoundLogic logic = session.RoundLogic;
      if (logic == null || !logic.RoundStarted) return false;
      return !ReferenceEquals(logic, endedLogic);
    }

    // Index du round courant, tel que le jeu le compte (0 pour le premier).
    // Incremente par Session.GotoNextRound avant le chargement du niveau suivant,
    // donc deja a jour quand on capture la premiere frame du round.
    private static int RoundOf(Level level)
    {
      Session session = level.Session;
      if (session == null) return 0;
      return session.RoundIndex < 0 ? 0 : session.RoundIndex;
    }

    private static void CaptureFrame(TFModFortRiseRecordSettings settings, Level level)
    {
      int round = RoundOf(level);

      // Grille de solides : une seule fois par nouveau niveau (geometrie statique).
      if (settings.recordState && !ReferenceEquals(level, lastLevel))
      {
        lastLevel = level;
        try
        {
          string gridPath = Path.Combine(sessionDir, RecorderWriter.RoundPrefix(round) + "level.json");
          File.WriteAllText(gridPath, StateCapture.BuildLevelGridJson(level));
        }
        catch (Exception e) { Logger.Error("MatchRecorder.grid: " + e); }
      }

      FrameJob job = new FrameJob { FrameIndex = frameIndex, Round = round, Width = Width, Height = Height };
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
        job.InputsLine = StateCapture.BuildInputsLine(frameIndex, round);
        any = true;
      }

      if (settings.recordState)
      {
        job.StateLine = StateCapture.BuildStateLine(frameIndex, round, level);
        any = true;
      }

      if (any && writer != null)
      {
        writer.Enqueue(job);
        frameIndex++;
      }
    }

    // Copie rapide GPU->CPU des pixels dans un buffer emprunte au pool.
    //
    // Le tampon est dimensionne sur le render target reel et non sur 320x240 : un
    // mod qui elargit l'ecran (WiderSet) donne une cible plus grande, et lire une
    // region plus grande que le tampon faisait ecrire FNA3D au-dela de celui-ci -
    // le GPU tombait alors en VK_ERROR_DEVICE_LOST.
    //
    // La surcharge explicite est preferee a GetData(buf), qui deduit le nombre
    // d'elements de la longueur du tableau : ici region et tampon sont accordes
    // dans le meme appel, et ne peuvent plus diverger en silence.
    private static void MSColorFill(RenderTarget2D rt, FrameJob job)
    {
      int count = rt.Width * rt.Height;

      var buf = RecorderWriter.RentBuffer(count);
      if (buf == null) return;

      rt.GetData<Microsoft.Xna.Framework.Color>(0, null, buf, 0, count);
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

    // Traduit le reglage utilisateur en niveau zlib. PNG etant sans perte,
    // ce choix ne joue que sur la taille des fichiers et le CPU du thread
    // d'ecriture (voir les mesures dans TFModFortRiseRecordSettings).
    private static CompressionLevel CompressionOf(TFModFortRiseRecordSettings settings)
    {
      switch (settings.recordPngCompression)
      {
        case TFModFortRiseRecordSettings.CompressionFast: return CompressionLevel.Fastest;
        case TFModFortRiseRecordSettings.CompressionSmallest: return CompressionLevel.SmallestSize;
        default: return CompressionLevel.Optimal;
      }
    }

    private static void StartSession(TFModFortRiseRecordSettings settings)
    {
      string baseDir = TFModFortRiseRecordModule.RecordingsPath;
      sessionDir = Path.Combine(baseDir, "match_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
      Directory.CreateDirectory(sessionDir);
      frameIndex = 0;
      accumulator = 0.0;
      lastLevel = null;
      endedLogic = null;
      int gifColors, gifEvery;
      settings.GetGifProfile(out gifColors, out gifEvery);
      writer = new RecorderWriter(sessionDir, CompressionOf(settings),
          settings.recordGif && settings.recordImages,
          settings.recordFps, gifEvery, gifColors);
      matchActive = true;
      capturing = true;
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
      capturing = false;
      sessionDir = null;
      lastLevel = null;
      endedLogic = null;
    }
  }
}
