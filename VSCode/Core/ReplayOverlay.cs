using System;
using Microsoft.Xna.Framework;
using Monocle;
using TowerFall;

namespace TFModFortRiseRecord
{
  /// <summary>
  /// La relecture par-dessus la partie, ouverte depuis le menu de pause.
  ///
  /// Elle ne QUITTE pas le match, et c'est tout l'enjeu : ouvrir le lecteur du menu
  /// principal depuis une partie en cours reviendrait a l'abandonner. La relecture se
  /// pose donc par-dessus le niveau, comme une fenetre, et le rend intact en se
  /// fermant.
  ///
  /// Elle vit sur la COUCHE 4, celle du menu de pause, et pas sur la couche du jeu.
  /// C'est ce qui la fait vivre pendant la pause : le setter de Level.Paused eteint
  /// toutes les couches sauf la 4 et la 10, donc une entite posee ailleurs cesserait
  /// simplement d'etre mise a jour des l'ouverture du menu.
  ///
  /// Elle relit les memes PNG que le lecteur du menu, avec le meme
  /// <see cref="ReplayReader"/> : c'est la meme chose vue au meme endroit, et il n'y a
  /// aucune raison d'en ecrire deux.
  ///
  /// A ne pas confondre avec le REPLAY du jeu : celui-ci rejoue les dernieres secondes
  /// depuis l'etat du jeu, en mouvement et sans HUD. La relecture de match, elle,
  /// montre les IMAGES telles qu'elles ont ete vues, depuis le debut, et se met en
  /// pause.
  /// </summary>
  public class ReplayOverlay : Entity
  {
    /// <summary>La couche du menu de pause : la seule qui survit a la pause.</summary>
    private const int LAYER = 4;

    /// <summary>Devant absolument tout, HUD compris.</summary>
    private const int DEPTH = -1000000;

    private static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f };
    private const int NORMAL_SPEED = 2;

    /// <summary>Duree d'un saut avant/arriere, en secondes de jeu.</summary>
    private const float SEEK_SECONDS = 2f;

    /// <summary>La relecture ouverte, ou null. Sert a ne pas en ouvrir deux.</summary>
    public static ReplayOverlay Current;

    private readonly ReplayReader reader;
    private readonly Action onClose;
    private readonly ReplayClock clock;

    private bool paused;
    private int speed = NORMAL_SPEED;
    private float carry;

    /// <summary>Le temps de jeu qu'on emprunte le temps de regarder. Voir ReplayTimeRate.</summary>
    private ReplayTimeRate timeRate;

    private ReplayOverlay(ReplayReader reader, Action onClose, string label) : base(LAYER)
    {
      Depth = DEPTH;
      this.reader = reader;
      this.onClose = onClose;
      clock = new ReplayClock(label, reader.Fps);
    }

    public static bool IsOpen => Current != null && Current.Scene == Engine.Instance.Scene;

    /// <summary>
    /// Ouvre la relecture. <paramref name="wholeMatch"/> vrai pour tout le match depuis
    /// son debut, faux pour la seule manche en cours.
    ///
    /// Rend faux quand il n'y a rien a relire - enregistrement coupe, ou aucune image
    /// encore ecrite. L'appelant remet alors son menu en place plutot que d'ouvrir une
    /// fenetre vide.
    /// </summary>
    public static bool Open(Level level, bool wholeMatch, Action onClose)
    {
      if (level == null || IsOpen)
      {
        return false;
      }

      string session = MatchRecorder.CurrentSession;

      if (string.IsNullOrEmpty(session))
      {
        return false;
      }

      // Les images partent sur un fil de fond : sans cette attente on ne relirait que
      // le debut de la manche.
      MatchRecorder.FlushNow();

      string round = wholeMatch ? null : MatchRecorder.CurrentRoundPrefix(level.Session);
      ReplayReader reader = ReplayReader.Open(session, round);

      // La manche en cours peut n'avoir aucune image - on vient de la commencer. Plutot
      // que de ne rien montrer, on retombe sur le match entier : ce qu'on veut revoir
      // est de toute facon juste avant.
      if (reader == null && !wholeMatch)
      {
        reader = ReplayReader.Open(session, null);
      }

      if (reader == null)
      {
        return false;
      }

      reader.Start();

      var overlay = new ReplayOverlay(reader, onClose, wholeMatch ? "match" : "manche");
      Current = overlay;
      level.Add(overlay);

      Sounds.ui_pause.Play(160f);
      return true;
    }

    public override void Added()
    {
      base.Added();
      timeRate.Hold();
    }

    public override void Removed()
    {
      base.Removed();

      timeRate.Release();
      reader?.Dispose();

      if (Current == this)
      {
        Current = null;
      }

      Sounds.ui_unpause.Play(160f);
      onClose?.Invoke();
    }

    public override void Update()
    {
      base.Update();

      MenuInput.Update();

      if (MenuInput.Back)
      {
        RemoveSelf();
        return;
      }

      if (MenuInput.Confirm)
      {
        // Arrive au bout, CONFIRMER ne peut plus vouloir dire "reprendre" : il n'y a
        // plus rien devant. Il veut dire "revoir".
        if (Finished)
        {
          Restart();
        }
        else
        {
          paused = !paused;
          Sounds.ui_click.Play(160f, 1f);
        }

        return;
      }

      if (MenuInput.Left)
      {
        Step(paused ? -1 : -Seek);
        return;
      }

      if (MenuInput.Right)
      {
        Step(paused ? 1 : Seek);
        return;
      }

      if (MenuInput.Up && speed < Speeds.Length - 1)
      {
        speed++;
        Sounds.ui_move1.Play(160f, 1f);
        return;
      }

      if (MenuInput.Down && speed > 0)
      {
        speed--;
        Sounds.ui_move1.Play(160f, 1f);
        return;
      }

      if (paused)
      {
        return;
      }

      // La cadence de PRISE DE VUE, mesuree sur l'horloge et non sur le temps de jeu.
      // Voir ReplayClock.
      int take = clock.Take(Speeds[speed], ref carry);

      for (int i = 0; i < take; i++)
      {
        if (!reader.Advance())
        {
          carry = 0f;
          break;
        }
      }

      if (reader.AtEnd)
      {
        paused = true;
      }
    }

    /// <summary>Le saut, en images : deux secondes a la cadence de l'enregistrement.</summary>
    private int Seek => Math.Max(1, (int)(SEEK_SECONDS * reader.Fps));

    /// <summary>
    /// La lecture est arrivee au bout et s'y est arretee.
    ///
    /// La pause seule ne suffit pas a le dire : on peut avoir mis en pause n'importe ou.
    /// C'est la conjonction des deux qui distingue "arrete par choix" de "termine".
    /// </summary>
    private bool Finished => paused && reader.AtEnd;

    /// <summary>Repart du debut, en lecture.</summary>
    private void Restart()
    {
      Sounds.ui_click.Play(160f, 1f);
      paused = false;
      Step(-reader.Count);
    }

    private void Step(int by)
    {
      reader.SeekTo(Math.Clamp(reader.FrameIndex + by, 0, reader.Count - 1));

      // Le report est remis a zero : il compte les images de la lecture qu'on vient
      // d'abandonner, et le laisser ferait sauter une image de plus a l'arrivee.
      carry = 0f;

      for (int i = 0; i < 60 && !reader.Advance(); i++)
      {
        System.Threading.Thread.Sleep(2);
      }
    }

    public override void Render()
    {
      base.Render();

      ReplayView.DrawFrame(reader.Frame);

      string hint = Finished
          ? "BACK: CLOSE   CONFIRM: WATCH AGAIN   L/R: STEP"
          : "BACK: CLOSE   CONFIRM: PAUSE   L/R: SEEK   U/D: SPEED";

      string left = Finished ? "END" : (paused ? "PAUSED " : "") + Label();

      ReplayView.DrawHud(hint, left, (reader.FrameIndex + 1) + " / " + reader.Count, 1f);
    }

    private string Label()
    {
      return Speeds[speed] switch
      {
        0.25f => "x0.25",
        0.5f => "x0.5",
        1f => "x1",
        2f => "x2",
        _ => "x4"
      };
    }
  }
}
