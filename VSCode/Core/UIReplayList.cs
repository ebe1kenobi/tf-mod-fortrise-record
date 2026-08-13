using System;
using System.Collections.Generic;
using System.IO;
using FortRise;
using Microsoft.Xna.Framework;
using Monocle;
using TowerFall;

namespace TFModFortRiseRecord
{
  /// <summary>
  /// La liste des enregistrements, ouverte depuis les reglages du mod.
  ///
  /// L'entree se fait par un bouton de l'ecran des reglages et non par une lame du
  /// menu principal : les lames se suivent sans interstice, QUIT touche deja le bas de
  /// l'ecran, et en inserer une obligerait a deplacer celles des autres mods. Les
  /// reglages du mod sont de toute facon l'endroit ou l'on va chercher ce que le mod
  /// sait faire.
  /// </summary>
  public class UIReplayList : CustomMenuState
  {
    private const float FirstRowY = 54f;
    private const float RowStep = 15f;
    private const float RowX = 24f;

    /// <summary>
    /// L'enregistrement a ouvrir. Passe par un champ statique parce que MainMenu
    /// instancie les etats lui-meme : il n'y a pas de place pour un argument.
    /// </summary>
    internal static string Opening;

    public UIReplayList(MainMenu main) : base(main)
    {
    }

    public override void Create()
    {
      // Retour a l'ecran des reglages du mod, d'ou l'on vient : c'est l'etat Options
      // avec le filtre de mod encore pose, donc il se reconstruit sur nos reglages.
      Main.BackState = MainMenu.MenuState.Options;
      Main.TweenBGCameraToY(2);

      List<string> found = Recordings();

      if (found.Count == 0)
      {
        Main.Add(new ReplayHint(new Vector2(160f, 120f)));
        Main.MaxUICameraY = 0f;
        Main.ToStartSelected = null;
        return;
      }

      var rows = new List<ReplayRow>();

      for (int i = 0; i < found.Count; i++)
      {
        string directory = found[i];
        float y = FirstRowY + i * RowStep;

        var row = new ReplayRow(new Vector2(RowX, y),
            new Vector2(i % 2 == 0 ? -280f : 600f, y), Label(directory), Length(directory));

        row.OnConfirmed = () =>
        {
          Opening = directory;
          Main.State = ModRegisters.MenuState<UIReplayPlayer>();
        };

        rows.Add(row);
      }

      for (int i = 0; i < rows.Count; i++)
      {
        if (i > 0) { rows[i].UpItem = rows[i - 1]; }
        if (i + 1 < rows.Count) { rows[i].DownItem = rows[i + 1]; }
      }

      Main.Add(rows);

      // La borne doit laisser passer ce que la derniere ligne demandera en se
      // selectionnant - Y - 120 - sinon la camera est bridee avant d'y arriver et les
      // dernieres lignes ne montent jamais a l'ecran.
      float lastY = FirstRowY + (rows.Count - 1) * RowStep;
      Main.MaxUICameraY = Math.Max(0f, lastY - 120f);
      Main.ToStartSelected = rows[0];
    }

    public override void Destroy()
    {
    }

    /// <summary>
    /// Les enregistrements, du plus recent au plus ancien : c'est celui qu'on vient
    /// de jouer qu'on veut revoir, pas celui d'il y a trois semaines.
    /// </summary>
    private static List<string> Recordings()
    {
      var found = new List<string>();

      try
      {
        string root = TFModFortRiseRecordModule.RecordingsPath;

        if (!Directory.Exists(root))
        {
          return found;
        }

        found.AddRange(Directory.GetDirectories(root));

        // Le nom porte l'horodatage - versus_20260813_140424 - donc l'ordre
        // alphabetique inverse est l'ordre chronologique inverse, sans avoir a
        // interroger le disque sur chaque dossier.
        found.Sort((a, b) => string.Compare(Path.GetFileName(b), Path.GetFileName(a),
            StringComparison.OrdinalIgnoreCase));
      }
      catch (Exception e)
      {
        Logger.Info("[Replay] liste illisible : " + e.Message);
      }

      return found;
    }

    /// <summary>"VERSUS - 13/08 14:04", lisible plutot que brut.</summary>
    private static string Label(string directory)
    {
      string name = Path.GetFileName(directory);
      string[] parts = name.Split('_');

      if (parts.Length < 3 || parts[1].Length != 8 || parts[2].Length < 6)
      {
        return name.ToUpperInvariant();
      }

      string day = parts[1].Substring(6, 2) + "/" + parts[1].Substring(4, 2);
      string time = parts[2].Substring(0, 2) + ":" + parts[2].Substring(2, 2);

      return parts[0].ToUpperInvariant() + " - " + day + " " + time;
    }

    /// <summary>
    /// La duree de l'enregistrement, en secondes.
    ///
    /// Le compte d'images ne suffit pas : le mod filme a la cadence reglee, quinze par
    /// defaut, pas a celle du jeu. Divisee par soixante, la duree affichee etait quatre
    /// fois trop courte.
    /// </summary>
    private static int Length(string directory)
    {
      try
      {
        int frames = Directory.GetFiles(directory, "*frame_*.png").Length;
        return frames / ReplayReader.ReadFps(directory);
      }
      catch (Exception)
      {
        return 0;
      }
    }
  }

  /// <summary>Une ligne de la liste : le match a gauche, son nombre d'images a droite.</summary>
  public class ReplayRow : MenuItem
  {
    private readonly string label;
    private readonly int seconds;
    private readonly Vector2 tweenFrom;
    private readonly Vector2 tweenTo;
    private readonly Wiggler wiggler;

    public Action OnConfirmed;

    public ReplayRow(Vector2 position, Vector2 from, string label, int seconds) : base(position)
    {
      this.label = label;
      this.seconds = seconds;

      tweenTo = position;
      tweenFrom = from;
      Position = from;

      wiggler = Wiggler.Create(20, 4f, null, null, false, false);
      Add(wiggler);
    }

    public override void Render()
    {
      base.Render();

      Color color = Selected ? VariantItem.ActiveSelection : Color.Gray;
      float scale = 1f + wiggler.Value * 0.15f;

      Draw.OutlineTextJustify(TFGame.Font, label, Position, color, Color.Black,
          new Vector2(0f, 0.5f), scale);

      // La duree plutot que le nombre d'images : "2:14" se lit, "8040" non.
      string length = seconds <= 0 ? "-" : (seconds / 60) + ":" + (seconds % 60).ToString("D2");

      Draw.OutlineTextJustify(TFGame.Font, length, Position + new Vector2(268f, 0f),
          color, Color.Black, new Vector2(1f, 0.5f), 1f);
    }

    public override void TweenIn()
    {
      Tween tween = Tween.Create(Tween.TweenMode.Oneshot, Ease.CubeOut, 20, true);
      tween.OnUpdate = t => Position = Vector2.Lerp(tweenFrom, tweenTo, t.Eased);
      Add(tween);
    }

    public override void TweenOut()
    {
      Tween tween = Tween.Create(Tween.TweenMode.Oneshot, Ease.CubeIn, 12, true);
      tween.OnUpdate = t => Position = Vector2.Lerp(tweenTo, tweenFrom, t.Eased);
      Add(tween);
    }

    protected override void OnConfirm()
    {
      Sounds.ui_click.Play(160f, 1f);
      wiggler.Start();
      OnConfirmed?.Invoke();
    }

    protected override void OnSelect()
    {
      wiggler.Start();

      // C'est la LIGNE qui fait defiler, pas le menu : MainMenu ne suit pas la
      // selection tout seul, il se contente de brider la camera a MaxUICameraY. Sans
      // cet appel, les lignes au-dela du bas de l'ecran restaient selectionnables mais
      // invisibles. C'est l'idiome des items de variantes du jeu.
      MainMenu?.TweenUICameraToY(Math.Max(0f, Y - 120f), 10);
    }

    protected override void OnDeselect()
    {
    }
  }

  /// <summary>Ce qu'on affiche quand il n'y a rien a relire.</summary>
  public class ReplayHint : Entity
  {
    private readonly Vector2 at;

    public ReplayHint(Vector2 position) : base(0)
    {
      at = position;
      Depth = -100;
    }

    public override void Render()
    {
      base.Render();

      Draw.OutlineTextCentered(TFGame.Font, "NO RECORDING YET",
          at + new Vector2(0f, -10f), Color.White, Color.Black, 1f);

      Draw.OutlineTextCentered(TFGame.Font, "PLAY A MATCH WITH RECORDING ON",
          at + new Vector2(0f, 4f), Color.Gray, Color.Black, 1f);
    }
  }
}
