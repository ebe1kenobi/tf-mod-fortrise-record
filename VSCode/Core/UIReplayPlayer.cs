using System;
using FortRise;
using Microsoft.Xna.Framework;
using Monocle;
using TowerFall;

namespace TFModFortRiseRecord
{
  /// <summary>
  /// La lecture d'un enregistrement : les images du match, une par une, plein ecran.
  ///
  /// Les PNG et non le GIF, et c'est un choix. Le GIF est quantifie a un petit nombre
  /// de couleurs et decime par le reglage d'intervalle : c'est un export fait pour
  /// etre partage. Les PNG, eux, sont l'enregistrement lui-meme, a la resolution et
  /// au rythme du jeu - et ils donnent gratuitement la pause, l'avance image par
  /// image et la vitesse variable, qu'un GIF ne saurait pas rendre.
  ///
  /// Les images font 320x240, exactement la resolution du jeu : elles se posent donc
  /// sans mise a l'echelle ni bordure.
  /// </summary>
  public class UIReplayPlayer : CustomMenuState
  {
    /// <summary>Les vitesses proposees, en images de replay par image de jeu.</summary>
    private static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f };

    private const int NORMAL_SPEED = 2;

    /// <summary>Duree d'un saut avant/arriere, en secondes de jeu.</summary>
    private const float SEEK_SECONDS = 2f;

    private ReplayReader reader;
    private ReplayClock clock;
    private ReplayTimeRate timeRate;
    private bool paused;
    private int speed = NORMAL_SPEED;
    private float carry;
    private float hudFade = 180f;

    public UIReplayPlayer(MainMenu main) : base(main)
    {
    }

    public override void Create()
    {
      // Retour a la liste du JOUR et non a celle des jours : on revient d'ou l'on
      // vient, ce qui evite de refaire tout le chemin pour voir la partie suivante.
      MainMenu.MenuState listState = ModRegisters.MenuState<UIReplayDay>();

      Main.BackState = listState;
      Main.TweenBGCameraToY(2);
      Main.ToStartSelected = null;
      Main.MaxUICameraY = 0f;

      reader = ReplayReader.Open(UIReplayDay.Opening);

      if (reader == null)
      {
        Main.State = listState;
        return;
      }

      reader.Start();
      clock = new ReplayClock("menu", reader.Fps);

      // Le mod accelerate peut avoir laisse le temps de jeu au double ou au triple en
      // quittant une partie. Voir ReplayTimeRate.
      timeRate.Hold();

      paused = false;
      speed = NORMAL_SPEED;
      carry = 0f;
      hudFade = 180f;

      Main.Add(new ReplayScreen(this));
    }

    public override void Destroy()
    {
      // Le fil de lecture et la texture courante meurent avec l'ecran : les laisser
      // tourner tiendrait un fichier ouvert et une texture vivante pour rien.
      reader?.Dispose();
      reader = null;
      timeRate.Release();
    }

    /// <summary>
    /// Avance la lecture. Appelee par l'entite d'affichage, une fois par image :
    /// CustomMenuState n'a pas d'update a lui.
    /// </summary>
    public void Tick()
    {
      if (reader == null)
      {
        return;
      }

      if (hudFade > 0f)
      {
        hudFade -= Engine.TimeMult;
      }

      Controls();

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
          // Rien de pret : le disque n'a pas suivi. On garde l'image precedente et on
          // reessaie, plutot que de sauter ou de montrer du noir.
          carry = 0f;
          break;
        }
      }

      if (reader.AtEnd)
      {
        paused = true;
      }
    }

    private void Controls()
    {
      if (MenuInput.Confirm)
      {
        hudFade = 180f;

        // Arrive au bout, CONFIRMER ne peut plus vouloir dire "reprendre" : il n'y a
        // plus rien devant. Il veut dire "revoir".
        if (Finished)
        {
          Sounds.ui_click.Play(160f, 1f);
          paused = false;
          Step(-reader.Count);
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
        hudFade = 180f;
        Sounds.ui_move1.Play(160f, 1f);
        return;
      }

      if (MenuInput.Down && speed > 0)
      {
        speed--;
        hudFade = 180f;
        Sounds.ui_move1.Play(160f, 1f);
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
    internal bool Finished => reader != null && paused && reader.AtEnd;

    private void Step(int by)
    {
      int at = Math.Clamp(reader.FrameIndex + by, 0, reader.Count - 1);

      reader.SeekTo(at);
      hudFade = 180f;

      // Le report compte les images de la lecture qu'on vient d'abandonner : le garder
      // ferait sauter une image de plus a l'arrivee.
      carry = 0f;

      // On veut voir tout de suite ou l'on vient d'aller, en pause comme en lecture :
      // on attend brievement que le fil de fond ait sorti l'image demandee. Sans cette
      // attente, la lecture reprenait sur l'image precedente et le saut semblait ne
      // rien faire.
      for (int i = 0; i < 60 && !reader.Advance(); i++)
      {
        System.Threading.Thread.Sleep(2);
      }
    }

    internal ReplayReader Reader => reader;

    internal bool Paused => paused;

    internal string SpeedLabel => Speeds[speed] switch
    {
      0.25f => "x0.25",
      0.5f => "x0.5",
      1f => "x1",
      2f => "x2",
      _ => "x4"
    };

    internal float HudAlpha => hudFade > 0f ? Math.Min(1f, hudFade / 60f) : (Paused ? 1f : 0f);

    internal string Hint => Finished
        ? "BACK: CLOSE   CONFIRM: WATCH AGAIN   L/R: STEP"
        : "CONFIRM: PAUSE   L/R: SEEK   U/D: SPEED";
  }

  /// <summary>
  /// L'entite qui fait avancer la lecture et pose l'image a l'ecran.
  ///
  /// Un CustomMenuState n'a ni Update ni Render a lui - MainMenu ne fait que le
  /// construire et le detruire - donc tout ce qui bat au rythme des images doit vivre
  /// dans une entite.
  /// </summary>
  public class ReplayScreen : Entity
  {
    private readonly UIReplayPlayer owner;

    public ReplayScreen(UIReplayPlayer owner) : base(0)
    {
      this.owner = owner;

      // Devant tout le reste du menu : c'est le sujet de l'ecran.
      Depth = -200000;
    }

    public override void Update()
    {
      base.Update();
      owner.Tick();
    }

    public override void Render()
    {
      base.Render();

      ReplayReader reader = owner.Reader;

      if (reader == null)
      {
        return;
      }

      ReplayView.DrawFrame(reader.Frame);

      // Le bandeau s'efface tout seul quand on regarde : il ne sert qu'au moment ou
      // l'on touche a quelque chose.
      float alpha = owner.HudAlpha;

      if (alpha <= 0f)
      {
        return;
      }

      string left = owner.Finished ? "END" : (owner.Paused ? "PAUSED " : "") + owner.SpeedLabel;

      ReplayView.DrawHud(owner.Hint, left,
          (reader.FrameIndex + 1) + " / " + reader.Count, alpha);
    }
  }
}
