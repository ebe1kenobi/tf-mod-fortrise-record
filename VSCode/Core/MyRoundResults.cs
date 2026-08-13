using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using FortRise;
using HarmonyLib;
using Monocle;
using TowerFall;

namespace TFModFortRiseRecord
{
  /// <summary>
  /// La relecture depuis l'ecran de fin de manche, sur le bouton RETOUR.
  ///
  /// Cet ecran a deja fait tomber deux tentatives, et les deux raisons valent d'etre
  /// retenues.
  ///
  /// La premiere version l'ecoutait par un PREFIXE qui rendait faux. Un prefixe qui rend
  /// faux n'annule pas seulement la methode d'origine : il annule aussi les prefixes des
  /// mods enregistres apres lui. Deux autres mods se greffent sur cette meme methode, et
  /// l'ecran entier s'est fige. Ici c'est un POSTFIXE : il ne peut rien annuler, il ne
  /// fait qu'ajouter.
  ///
  /// La seconde version cherchait une touche libre et n'en trouvait pas. Toutes les
  /// touches lues par cet ecran sont prises : CONFIRMER continue, ALT relance le replay
  /// du jeu, SAVE REPLAY le sauve - et ce dernier est le meme bouton physique que ALT2,
  /// sur lequel un autre mod ouvre deja son tableau de statistiques.
  ///
  /// RETOUR est le seul que cet ecran ne lit jamais. C'est donc le seul qu'on puisse
  /// prendre sans rien deposseder.
  /// </summary>
  public class MyRoundResults : IHookable
  {
    private const int GUIDE_SLOT = 3;

    private static readonly FieldInfo FinishedField =
        AccessTools.DeclaredField(typeof(VersusRoundResults), "finished");

    private static readonly FieldInfo FocusedField =
        AccessTools.DeclaredField(typeof(VersusRoundResults), "focused");

    /// <summary>
    /// L'indication de touche ajoutee, par ecran.
    ///
    /// Une table faible et non un champ statique : chaque manche construit un nouvel
    /// ecran, et retenir le dernier en dur retiendrait aussi tous les niveaux qu'il
    /// traine derriere lui.
    /// </summary>
    private static readonly ConditionalWeakTable<VersusRoundResults, MenuButtonGuide> guides =
        new ConditionalWeakTable<VersusRoundResults, MenuButtonGuide>();

    public static void Load(IHarmony harmony)
    {
      harmony.Patch(
          AccessTools.DeclaredMethod(typeof(VersusRoundResults), "Update"),
          postfix: new HarmonyMethod(typeof(MyRoundResults), nameof(Update_postfix))
      );
    }

    public static void Update_postfix(VersusRoundResults __instance)
    {
      if (FinishedField == null || FocusedField == null)
      {
        return;
      }

      // Avant la fin de l'animation des points, l'ecran ne repond a rien : y poser une
      // indication de touche promettrait quelque chose qui ne marche pas encore.
      if (!(FinishedField.GetValue(__instance) is bool finished) || !finished)
      {
        return;
      }

      if (!guides.TryGetValue(__instance, out MenuButtonGuide guide))
      {
        // Rien n'a ete enregistre : pas d'indication plutot qu'une touche qui ne fait
        // rien.
        if (string.IsNullOrEmpty(MatchRecorder.CurrentSession))
        {
          return;
        }

        guide = new MenuButtonGuide(GUIDE_SLOT, MenuButtonGuide.ButtonModes.Back, "MATCH REPLAY");
        __instance.Add(guide);
        guides.Add(__instance, guide);
      }

      // Les indications vanilla s'allument et s'eteignent ensemble, par un champ prive
      // auquel la notre n'appartient pas : c'est a nous de la suivre.
      bool focused = FocusedField.GetValue(__instance) is bool f && f;
      guide.Visible = focused && !ReplayOverlay.IsOpen;

      if (!focused || ReplayOverlay.IsOpen || !MenuInput.Back)
      {
        return;
      }

      Open(__instance);
    }

    /// <summary>
    /// Ouvre la relecture de la manche qui vient de finir, et endort l'ecran pendant ce
    /// temps.
    ///
    /// Sans cela l'ecran continuerait de lire les touches derriere la relecture, et
    /// CONFIRMER enchainerait sur la manche suivante alors qu'on regarde encore. Active
    /// a faux plutot que RemoveSelf : l'ecran doit revenir intact, scores compris.
    ///
    /// Le retour qui ferme la relecture ne risque pas de le reveiller en sursaut : cet
    /// ecran ne lit pas le retour, c'est justement pourquoi on l'a choisi.
    /// </summary>
    private static void Open(VersusRoundResults results)
    {
      if (!(results.Scene is Level level))
      {
        return;
      }

      results.Active = false;

      if (guides.TryGetValue(results, out MenuButtonGuide guide))
      {
        guide.Visible = false;
      }

      if (!ReplayOverlay.Open(level, false, () => results.Active = true))
      {
        results.Active = true;
      }
    }
  }
}
