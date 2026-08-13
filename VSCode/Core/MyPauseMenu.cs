using System;
using System.Reflection;
using FortRise;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Monocle;
using TowerFall;
using static TowerFall.PauseMenu;

namespace TFModFortRiseRecord
{
  /// <summary>
  /// L'entree vers la relecture, posee dans le MENU DE PAUSE.
  ///
  /// Le premier essai la mettait sur l'ecran de fin de manche, sur un bouton. Cet ecran
  /// n'a pas de menu : il n'y a que des touches nues, et elles sont deja toutes prises -
  /// CONFIRM continue, ALT relance le replay du jeu, ALT2 sauve ce replay et ouvre en
  /// meme temps le tableau des statistiques d'un autre mod. Toute touche ajoutee la
  /// entrait en conflit avec l'une d'elles, et le prefixe qui l'ecoutait a fini par
  /// figer l'ecran : un prefixe qui rend faux n'annule pas que la methode d'origine, il
  /// annule aussi les prefixes des mods enregistres apres lui.
  ///
  /// Le menu de pause n'a aucun de ces problemes. C'est une VRAIE liste d'items : on en
  /// ajoute un, il se selectionne comme les autres, et rien de ce qui existait ne change
  /// de sens. C'est aussi le seul endroit d'une partie en cours ou l'on s'arrete
  /// volontairement - donc le seul ou regarder une relecture ne coute pas une manche.
  ///
  /// Il apparait sur tous les menus de pause et de fin, versus comme solo : le mod
  /// enregistre les deux. Les menus de PAUSE relisent la manche en cours, les menus de
  /// FIN relisent tout ce qui a ete joue, alors complet.
  /// </summary>
  public class MyPauseMenu : IHookable
  {
    private const string ITEM_ROUND = "WATCH REPLAY";
    private const string ITEM_MATCH = "WATCH MATCH REPLAY";

    /// <summary>AddItem est prive : la seule facon de s'ajouter a la liste vanilla.</summary>
    private static readonly MethodInfo AddItem =
        AccessTools.DeclaredMethod(typeof(PauseMenu), "AddItem", new[] { typeof(string), typeof(Action) });

    private static readonly FieldInfo PanelField = AccessTools.DeclaredField(typeof(PauseMenu), "panel");

    private static readonly FieldInfo NamesField = AccessTools.DeclaredField(typeof(PauseMenu), "optionNames");

    public static void Load(IHarmony harmony)
    {
      harmony.Patch(
          AccessTools.DeclaredConstructor(typeof(PauseMenu), new[]
          {
            typeof(Level), typeof(Vector2), typeof(MenuType), typeof(int)
          }),
          postfix: new HarmonyMethod(typeof(MyPauseMenu), nameof(ctor_postfix))
      );
    }

    public static void ctor_postfix(PauseMenu __instance, Level level, MenuType menuType)
    {
      // En postfix : les items vanilla sont deja poses, le notre arrive donc en fin de
      // liste et ne decale la selection de personne.
      if (AddItem == null || level == null)
      {
        return;
      }

      // Les menus de FIN relisent tout ce qui a ete joue, les menus de PAUSE la seule
      // manche en cours. Le partage se fait sur le type et non sur le mode : le mod
      // enregistre aussi bien le versus que le quest, ou une manche est une vague.
      bool wholeMatch = End(menuType);

      // Rien n'a ete enregistre : pas d'item plutot qu'un item qui ne fait rien.
      if (string.IsNullOrEmpty(MatchRecorder.CurrentSession))
      {
        return;
      }

      try
      {
        AddItem.Invoke(__instance, new object[]
        {
          wholeMatch ? ITEM_MATCH : ITEM_ROUND,
          new Action(() => Open(__instance, level, wholeMatch))
        });

        Resize(__instance);
      }
      catch (Exception e)
      {
        Logger.Info("[Replay] item de pause refuse : " + e.Message);
      }
    }

    /// <summary>
    /// Redimensionne le cadre pour l'item ajoute.
    ///
    /// Le constructeur pose le panneau en dernier, a une hauteur calculee sur le nombre
    /// d'items du moment. Notre item arrive apres, en postfix : les items se
    /// repartissaient bien sur six lignes, mais le cadre en tenait cinq, et le premier
    /// comme le dernier debordaient sur sa bordure.
    ///
    /// Le panneau est remplace plutot que retaille : sa taille est fixee a la
    /// construction, et sa position en depend.
    /// </summary>
    private static void Resize(PauseMenu menu)
    {
      if (PanelField == null || NamesField == null)
      {
        return;
      }

      if (!(NamesField.GetValue(menu) is System.Collections.Generic.List<string> names))
      {
        return;
      }

      if (PanelField.GetValue(menu) is MenuPanel old)
      {
        menu.Remove(old);
      }

      // La formule est celle du jeu : dix par ligne, trente de bordure.
      var panel = new MenuPanel(120, names.Count * 10 + 30);
      menu.Add(panel);
      PanelField.SetValue(menu, panel);
    }

    /// <summary>Vrai pour les menus de fin de partie, ou plus rien ne sera enregistre.</summary>
    private static bool End(MenuType menuType)
    {
      switch (menuType)
      {
        case MenuType.VersusMatchEnd:
        case MenuType.TrialsComplete:
        case MenuType.TrialsFailure:
        case MenuType.QuestGameOver:
        case MenuType.QuestComplete:
        case MenuType.DarkWorldGameOver:
        case MenuType.DarkWorldComplete:
          return true;

        default:
          return false;
      }
    }

    /// <summary>
    /// Ouvre la relecture et met le menu de pause en sommeil pendant ce temps.
    ///
    /// Active a faux plutot que RemoveSelf : le menu doit revenir tel qu'il etait, avec
    /// sa selection et son titre. Monocle saute simplement les entites inactives, donc
    /// le menu cesse de lire les touches sans rien perdre.
    /// </summary>
    private static void Open(PauseMenu menu, Level level, bool wholeMatch)
    {
      Sounds.ui_click.Play(160f, 1f);

      menu.Active = false;
      menu.Visible = false;

      if (!ReplayOverlay.Open(level, wholeMatch, () => Restore(level, menu)))
      {
        menu.Active = true;
        menu.Visible = true;
      }
    }

    /// <summary>
    /// Rend la main au menu de pause, mais pas avant que la touche de retour soit
    /// relachee.
    ///
    /// Sans cette attente, le RETOUR qui ferme la relecture serait encore enfonce a
    /// l'image suivante : le menu, tout juste reveille, le lirait a son tour et
    /// reprendrait la partie. On sortirait donc de la relecture directement dans le jeu,
    /// sans repasser par la pause.
    /// </summary>
    private static void Restore(Level level, PauseMenu menu)
    {
      menu.Visible = true;
      level.Add(new WakeUp(menu));
    }

    /// <summary>Reveille le menu de pause des que plus rien n'est presse.</summary>
    private class WakeUp : Entity
    {
      private readonly PauseMenu menu;

      public WakeUp(PauseMenu menu) : base(4)
      {
        this.menu = menu;
      }

      public override void Update()
      {
        base.Update();

        MenuInput.Update();

        if (MenuInput.Back || MenuInput.ConfirmOrStart)
        {
          return;
        }

        menu.Active = true;
        RemoveSelf();
      }
    }
  }
}
