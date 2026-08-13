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
    private static Session lastSession;
    private static int endedRound = -1;
    private static int lastWave = -1;

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
      // La sortie d'un niveau. En versus elle suit EndRound et ne fait rien de
      // plus ; en coop c'est la SEULE borne de fin de round qui existe.
      harmony.Patch(
          AccessTools.DeclaredMethod(typeof(Session), nameof(Session.GotoNextRound)),
          prefix: new HarmonyMethod(GotoNextRound_prefix)
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
      CloseRound(__instance);
    }

    /// <summary>
    /// Sortie de niveau. En coop (Quest, Dark World) le jeu n'appelle JAMAIS
    /// Session.EndRound - seuls les trois RoundLogic versus le font - et un niveau
    /// termine enchaine directement sur GotoNextRound. Sans cette borne, un run
    /// entier finissait dans un seul round, sans jamais produire de GIF.
    ///
    /// En versus elle arrive apres EndRound, sur la meme RoundLogic : CloseRound
    /// s'en apercoit et ne fait rien.
    /// </summary>
    public static void GotoNextRound_prefix(Session __instance)
    {
      CloseRound(__instance);
    }

    /// <summary>
    /// Ferme le round en cours, une seule fois.
    ///
    /// La RoundLogic terminee est memorisee plutot qu'un simple booleen : elle est
    /// recreee a chaque Session.LevelLoadStart, donc le round suivant se distingue
    /// tout seul, sans remise a zero a gerer - et une seconde borne sur le meme
    /// round (EndRound puis GotoNextRound en versus) est ignoree.
    ///
    /// Le NUMERO est retenu en plus de la logique, pour la quete : ses vagues
    /// s'enchainent sous une seule et meme RoundLogic, et sans lui la deuxieme vague
    /// passerait pour la premiere, deja close.
    /// </summary>
    /// <summary>
    /// Le dossier du match en cours, ou null quand rien n'est enregistre.
    ///
    /// Expose pour la relecture depuis l'ecran de fin de manche : elle a besoin de
    /// savoir OU sont les images qu'on vient de produire, et personne d'autre ne le
    /// sait.
    /// </summary>
    public static string CurrentSession => sessionDir;

    /// <summary>Le prefixe des fichiers de la manche qui vient de finir.</summary>
    public static string CurrentRoundPrefix(Session session)
    {
      return RecorderWriter.RoundPrefix(Math.Max(0, RoundOf(session)));
    }

    /// <summary>
    /// Vide la file d'ecriture pour que tout ce qui a ete capture soit sur le disque.
    ///
    /// Sans cette attente, relire la manche juste finie n'en montrerait que le debut :
    /// les images partent sur un fil de fond, qui a plusieurs secondes de retard sur
    /// la fin de la manche.
    /// </summary>
    public static void FlushNow()
    {
      try
      {
        writer?.WaitForIdle();
      }
      catch (Exception e) { Logger.Error("MatchRecorder.FlushNow: " + e); }
    }

    private static void CloseRound(Session session)
    {
      CloseRound(session, RoundOf(session));
    }

    private static void CloseRound(Session session, int round)
    {
      if (session == null)
      {
        return;
      }

      if (ReferenceEquals(session.RoundLogic, endedLogic) && round == endedRound)
      {
        return;
      }

      endedLogic = session.RoundLogic;
      endedRound = round;

      // Toutes les frames du round sont deja dans la file : on y ajoute le
      // marqueur d'export, traite a son tour par le thread de fond.
      if (writer == null)
      {
        return;
      }

      try
      {
        writer.EnqueueGifExport(round < 0 ? 0 : round);
      }
      catch (Exception e) { Logger.Error("MatchRecorder.CloseRound: " + e); }
    }

    /// <summary>
    /// La vague de quete en cours, ou -1 hors quete.
    ///
    /// En quete, une manche n'existe pas : le jeu enchaine des VAGUES de monstres
    /// sous une seule RoundLogic, sans jamais appeler EndRound ni GotoNextRound. La
    /// vague est donc l'unite qui correspond a un round ailleurs - c'est elle qu'on
    /// numerote et qu'on exporte en GIF.
    /// </summary>
    private static int QuestWave(Session session)
    {
      QuestRoundLogic quest = session?.RoundLogic as QuestRoundLogic;
      if (quest == null)
      {
        return -1;
      }

      return quest.CurrentWave < 0 ? 0 : quest.CurrentWave;
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

      // Chaque famille de modes a son interrupteur. Un joueur qui ne filme que le
      // versus ne doit pas voir son disque se remplir en traversant Dark World.
      if (!ModeEnabled(settings, level))
      {
        if (matchActive) StopSession();
        return;
      }

      lastSession = level.Session;

      // Changement de vague en quete : la precedente est finie, on exporte son GIF.
      // La borne est prise au DEBUT de la vague suivante et non des le dernier
      // monstre tue : entre les deux, le jeu continue de tourner - on ramasse les
      // fleches, l'affichage des chiffres romains defile - et ces images-la
      // appartiennent encore a la vague qu'on vient de finir.
      int wave = QuestWave(level.Session);
      if (wave >= 0 && wave != lastWave)
      {
        if (lastWave >= 0 && matchActive)
        {
          CloseRound(level.Session, lastWave);
        }

        lastWave = wave;
      }

      bool live = IsRoundLive(level);

      // Debut de match : on ouvre la session au premier round reellement lance,
      // pas des le chargement du niveau (evite de filmer la cinematique FIGHT!).
      if (!matchActive)
      {
        if (!live) return;
        StartSession(settings, level);
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

    /// <summary>
    /// Ce mode-la doit-il etre filme ?
    ///
    /// La coupure est celle du jeu lui-meme : <c>MatchSettings.SoloMode</c> couvre
    /// Quest, Dark World, Trials et les tests de l'editeur - tout ce qui se joue
    /// contre le jeu - et le reste est du versus. S'aligner dessus plutot que
    /// d'enumerer les modes evite qu'un mode ajoute par un mod ne tombe dans aucune
    /// des deux cases et ne soit jamais filme.
    /// </summary>
    private static bool ModeEnabled(TFModFortRiseRecordSettings settings, Level level)
    {
      MatchSettings match = level.Session != null ? level.Session.MatchSettings : null;
      if (match == null)
      {
        return false;
      }

      return match.SoloMode ? settings.recordCoop : settings.recordVersus;
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
    //
    // En QUETE il n'y a pas de round : c'est la vague de monstres qui joue ce role.
    private static int RoundOf(Level level)
    {
      return RoundOf(level?.Session);
    }

    private static int RoundOf(Session session)
    {
      if (session == null) return 0;

      int wave = QuestWave(session);
      if (wave >= 0) return wave;

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

    private static void StartSession(TFModFortRiseRecordSettings settings, Level level)
    {
      string baseDir = TFModFortRiseRecordModule.RecordingsPath;

      // Le mode nomme le dossier : "darkworld_...", "quest_...", "headhunters_...".
      // Avec le coop, "match_" ne disait plus de quoi il s'agissait, et il fallait
      // ouvrir un GIF pour le savoir.
      sessionDir = Path.Combine(baseDir, ModeTag(level) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
      Directory.CreateDirectory(sessionDir);
      WriteFps(sessionDir, settings.recordFps);
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

    /// <summary>Le nom du fichier qui porte la cadence d'un enregistrement.</summary>
    public const string FpsFileName = "fps.txt";

    /// <summary>
    /// Note la cadence de prise de vue a cote des images.
    ///
    /// Sans elle, la relecture ne pouvait que supposer 60 - la cadence du jeu - alors
    /// que le mod filme a la cadence REGLEE, 15 par defaut. Une seconde de jeu tenant
    /// alors en quinze images, les rejouer a soixante les faisait defiler quatre fois
    /// trop vite, et les sauts de "deux secondes" en valaient huit.
    ///
    /// La cadence est ecrite par enregistrement et non lue dans les reglages au moment
    /// de relire : le reglage a pu changer entre-temps, et c'est celle de la prise de
    /// vue qui compte.
    /// </summary>
    private static void WriteFps(string directory, int fps)
    {
      try
      {
        File.WriteAllText(Path.Combine(directory, FpsFileName),
            (fps < 1 ? 1 : fps).ToString(System.Globalization.CultureInfo.InvariantCulture));
      }
      catch (Exception e)
      {
        // La relecture retombera sur sa valeur par defaut : ce n'est pas une raison
        // pour renoncer a filmer.
        Logger.Info("[Record] cadence non ecrite : " + e.Message);
      }
    }

    /// <summary>Nom de mode utilisable dans un chemin, en minuscules.</summary>
    private static string ModeTag(Level level)
    {
      try
      {
        MatchSettings match = level != null && level.Session != null ? level.Session.MatchSettings : null;
        return match == null ? "match" : match.Mode.ToString().ToLowerInvariant();
      }
      catch (Exception)
      {
        return "match";
      }
    }

    private static void StopSession()
    {
      // Le dernier round n'a pas toujours de borne : une partie coop se termine par
      // un retour a la carte, sans GotoNextRound - quete achevee, ou tous les
      // joueurs morts. Sans ceci, le GIF du dernier niveau ne sortait jamais.
      CloseRound(lastSession);

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
      endedRound = -1;
      lastWave = -1;
      lastSession = null;
    }
  }
}
