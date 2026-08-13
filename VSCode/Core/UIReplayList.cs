using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FortRise;
using Microsoft.Xna.Framework;
using Monocle;
using TowerFall;

namespace TFModFortRiseRecord
{
  /// <summary>
  /// La liste des JOURS, ouverte depuis les reglages du mod.
  ///
  /// Un ecran de plus qu'avant, et c'est le but. Tout etait a plat, un enregistrement
  /// par ligne : apres quelques soirees, la liste depassait l'ecran et retrouver la
  /// partie de mardi demandait de lire des horodatages colles au nom du mode. On cherche
  /// une partie par sa DATE bien avant de la chercher par son mode, donc c'est la date
  /// qui range - un jour par ligne ici, les parties du jour a l'ecran suivant.
  ///
  /// L'entree se fait par un bouton de l'ecran des reglages et non par une lame du menu
  /// principal : les lames se suivent sans interstice, QUIT touche deja le bas de
  /// l'ecran, et en inserer une obligerait a deplacer celles des autres mods.
  /// </summary>
  public class UIReplayList : CustomMenuState
  {
    private const float FirstRowY = 54f;
    private const float RowStep = 15f;
    private const float RowX = 24f;

    /// <summary>
    /// Le jour a ouvrir. Passe par un champ statique parce que MainMenu instancie les
    /// etats lui-meme : il n'y a pas de place pour un argument.
    /// </summary>
    internal static string OpeningDay;

    /// <summary>
    /// Le message d'absence, a retirer en quittant l'ecran.
    ///
    /// MainMenu balaie les MenuItem tout seul en changeant d'etat, mais pas les
    /// entites ordinaires : celle-ci restait donc affichee par-dessus tous les ecrans
    /// suivants, indefiniment, une fois qu'elle etait apparue une premiere fois.
    /// </summary>
    private ReplayHint hint;

    public UIReplayList(MainMenu main) : base(main)
    {
    }

    public override void Create()
    {
      // Retour a l'ecran des reglages du mod, d'ou l'on vient : c'est l'etat Options
      // avec le filtre de mod encore pose, donc il se reconstruit sur nos reglages.
      Main.BackState = MainMenu.MenuState.Options;
      Main.TweenBGCameraToY(2);

      List<string> days = Days();

      if (days.Count == 0)
      {
        hint = new ReplayHint(new Vector2(160f, 120f));
        Main.Add(hint);
        Main.MaxUICameraY = 0f;
        Main.ToStartSelected = null;
        return;
      }

      var rows = new List<ReplayRow>();

      for (int i = 0; i < days.Count; i++)
      {
        string day = days[i];
        float y = FirstRowY + i * RowStep;

        var row = new ReplayRow(new Vector2(RowX, y),
            new Vector2(i % 2 == 0 ? -280f : 600f, y), DayLabel(day), Count(day) + " MATCHES");

        row.OnConfirmed = () =>
        {
          OpeningDay = day;
          Main.State = ModRegisters.MenuState<UIReplayDay>();
        };

        rows.Add(row);
      }

      ReplayRow.Link(Main, rows, FirstRowY, RowStep);
    }

    public override void Destroy()
    {
      hint?.RemoveSelf();
      hint = null;
    }

    /// <summary>
    /// Les jours qui contiennent quelque chose, du plus recent au plus ancien : c'est
    /// la soiree qu'on vient de jouer qu'on veut revoir, pas celle d'il y a trois
    /// semaines.
    /// </summary>
    private static List<string> Days()
    {
      var found = new List<string>();

      try
      {
        string root = TFModFortRiseRecordModule.RecordingsPath;

        if (!Directory.Exists(root))
        {
          return found;
        }

        foreach (string dir in Directory.GetDirectories(root))
        {
          // Seuls les dossiers de jour. Ce qui n'en est pas un est un enregistrement
          // que la migration n'a pas su ranger : il n'a pas sa place dans cette liste,
          // et il n'est pas efface pour autant.
          if (DateTime.TryParseExact(Path.GetFileName(dir), MatchRecorder.DayFormat,
                  CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
          {
            found.Add(dir);
          }
        }

        // Le nom EST la date, tirets compris : l'ordre alphabetique inverse est donc
        // l'ordre chronologique inverse, sans avoir a interroger le disque.
        found.Sort((a, b) => string.Compare(Path.GetFileName(b), Path.GetFileName(a),
            StringComparison.OrdinalIgnoreCase));
      }
      catch (Exception e)
      {
        Logger.Info("[Replay] liste des jours illisible : " + e.Message);
      }

      return found;
    }

    /// <summary>"WED 13/08" plutot que "2026-08-13" : on situe une soiree par son jour.</summary>
    private static string DayLabel(string directory)
    {
      string name = Path.GetFileName(directory);

      if (!DateTime.TryParseExact(name, MatchRecorder.DayFormat,
              CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day))
      {
        return name.ToUpperInvariant();
      }

      string weekday = day.ToString("ddd", CultureInfo.InvariantCulture).ToUpperInvariant();
      return weekday + " " + day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    private static int Count(string directory)
    {
      try
      {
        return Directory.GetDirectories(directory).Length;
      }
      catch (Exception)
      {
        return 0;
      }
    }
  }

  /// <summary>
  /// Les enregistrements d'un jour.
  ///
  /// Meme ecran que la liste des jours, a ceci pres que valider ouvre le lecteur. Ce
  /// sont les deux moities de ce qui etait une seule liste.
  /// </summary>
  public class UIReplayDay : CustomMenuState
  {
    private const float FirstRowY = 54f;
    private const float RowStep = 15f;
    private const float RowX = 24f;

    /// <summary>L'enregistrement a lire.</summary>
    internal static string Opening;

    /// <summary>Le message d'absence, a retirer en quittant l'ecran. Voir UIReplayList.</summary>
    private ReplayHint hint;

    public UIReplayDay(MainMenu main) : base(main)
    {
    }

    public override void Create()
    {
      MainMenu.MenuState dayList = ModRegisters.MenuState<UIReplayList>();

      Main.BackState = dayList;
      Main.TweenBGCameraToY(2);

      List<string> found = Recordings();

      if (found.Count == 0)
      {
        // Un jour vide ne devrait pas apparaitre dans la liste, mais le dossier a pu
        // etre vide a la main entre les deux ecrans.
        hint = new ReplayHint(new Vector2(160f, 120f));
        Main.Add(hint);
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

      ReplayRow.Link(Main, rows, FirstRowY, RowStep);
    }

    public override void Destroy()
    {
      hint?.RemoveSelf();
      hint = null;
    }

    /// <summary>Les parties du jour, de la plus recente a la plus ancienne.</summary>
    private static List<string> Recordings()
    {
      var found = new List<string>();

      try
      {
        if (string.IsNullOrEmpty(UIReplayList.OpeningDay)
            || !Directory.Exists(UIReplayList.OpeningDay))
        {
          return found;
        }

        found.AddRange(Directory.GetDirectories(UIReplayList.OpeningDay));

        // Le nom finit par l'heure - "headhunters_140424" - donc l'ordre alphabetique
        // inverse est l'ordre chronologique inverse a l'interieur d'un jour.
        found.Sort((a, b) => string.Compare(Time(b), Time(a), StringComparison.OrdinalIgnoreCase));
      }
      catch (Exception e)
      {
        Logger.Info("[Replay] jour illisible : " + e.Message);
      }

      return found;
    }

    /// <summary>L'heure brute d'un dossier de partie, ou son nom entier a defaut.</summary>
    private static string Time(string directory)
    {
      string name = Path.GetFileName(directory);
      int cut = name.LastIndexOf('_');
      return cut < 0 ? name : name.Substring(cut + 1);
    }

    /// <summary>"HEADHUNTERS 14:04", lisible plutot que brut.</summary>
    private static string Label(string directory)
    {
      string name = Path.GetFileName(directory);
      int cut = name.LastIndexOf('_');

      if (cut < 0 || name.Length - cut - 1 < 4)
      {
        return name.ToUpperInvariant();
      }

      string mode = name.Substring(0, cut).ToUpperInvariant();
      string time = name.Substring(cut + 1);

      return mode + " " + time.Substring(0, 2) + ":" + time.Substring(2, 2);
    }

    /// <summary>
    /// La duree de l'enregistrement, mise en forme.
    ///
    /// Le compte d'images ne suffit pas : le mod filme a la cadence reglee, quinze par
    /// defaut, pas a celle du jeu. Divisee par soixante, la duree affichee etait quatre
    /// fois trop courte.
    /// </summary>
    private static string Length(string directory)
    {
      try
      {
        int frames = Directory.GetFiles(directory, "*frame_*.png").Length;

        if (frames <= 0)
        {
          return "-";
        }

        int seconds = frames / ReplayReader.ReadFps(directory);
        return (seconds / 60) + ":" + (seconds % 60).ToString("D2");
      }
      catch (Exception)
      {
        return "-";
      }
    }
  }

  /// <summary>Une ligne de liste : un libelle a gauche, une precision a droite.</summary>
  public class ReplayRow : MenuItem
  {
    private readonly string label;
    private readonly string right;
    private readonly Vector2 tweenFrom;
    private readonly Vector2 tweenTo;
    private readonly Wiggler wiggler;

    public Action OnConfirmed;

    public ReplayRow(Vector2 position, Vector2 from, string label, string right) : base(position)
    {
      this.label = label;
      this.right = right;

      tweenTo = position;
      tweenFrom = from;
      Position = from;

      wiggler = Wiggler.Create(20, 4f, null, null, false, false);
      Add(wiggler);
    }

    /// <summary>
    /// Chaine les lignes, les pose et regle jusqu'ou la camera peut descendre.
    ///
    /// Commun aux deux ecrans : ils ont la meme liste, seul ce qu'on ouvre en validant
    /// les distingue.
    /// </summary>
    public static void Link(MainMenu main, List<ReplayRow> rows, float firstY, float step)
    {
      for (int i = 0; i < rows.Count; i++)
      {
        if (i > 0) { rows[i].UpItem = rows[i - 1]; }
        if (i + 1 < rows.Count) { rows[i].DownItem = rows[i + 1]; }
      }

      main.Add(rows);

      // La borne doit laisser passer ce que la derniere ligne demandera en se
      // selectionnant - Y - 120 - sinon la camera est bridee avant d'y arriver et les
      // dernieres lignes ne montent jamais a l'ecran.
      float lastY = firstY + (rows.Count - 1) * step;
      main.MaxUICameraY = Math.Max(0f, lastY - 120f);
      main.ToStartSelected = rows[0];
    }

    public override void Render()
    {
      base.Render();

      Color color = Selected ? VariantItem.ActiveSelection : Color.Gray;
      float scale = 1f + wiggler.Value * 0.15f;

      Draw.OutlineTextJustify(TFGame.Font, label, Position, color, Color.Black,
          new Vector2(0f, 0.5f), scale);

      Draw.OutlineTextJustify(TFGame.Font, right, Position + new Vector2(268f, 0f),
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
