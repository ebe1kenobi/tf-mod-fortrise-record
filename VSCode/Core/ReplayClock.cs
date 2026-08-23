using System;
using Monocle;

namespace TFModFortRiseRecord
{
  /// <summary>
  /// La base de temps de la relecture : l'HORLOGE, pas le temps de jeu.
  ///
  /// **Les deux lecteurs avancaient sur <c>Engine.TimeMult</c>, et c'etait une erreur
  /// de nature.** Ce multiplicateur est du temps de JEU : il vaut un a soixante images
  /// par seconde, mais le moteur le tord - il est divise par deux pendant un ralenti
  /// d'orbe, il suit la duree reelle de chaque image quand elle s'allonge, et il est
  /// force a un quand le jeu s'estime en retard. Une relecture n'a aucune raison de
  /// suivre ces variations : les images ont ete prises a une cadence fixe, elles
  /// doivent revenir a cette cadence, quoi que fasse la partie autour.
  ///
  /// On lit donc <c>Engine.DeltaTicks</c>, la seule mesure que le moteur ne retouche
  /// pas - la duree brute de l'image, telle que le framework la donne. La vitesse x1
  /// veut alors dire "comme c'etait", partout et dans tous les etats du jeu.
  ///
  /// **Le journal est la pour la difference qui reste.** Une relecture de match entier
  /// parait plus rapide qu'une relecture de manche, alors que le code de cadence est
  /// le meme des deux cotes - meme lecteur, meme dossier, meme cadence relue. Faute
  /// d'avoir trouve la cause en lisant, on mesure : une ligne par seconde de
  /// relecture, avec ce qui est reellement consomme. Si l'ecart survit a ce changement
  /// de base de temps, cette ligne le nommera.
  /// </summary>
  internal sealed class ReplayClock
  {
    private const double TicksPerSecond = TimeSpan.TicksPerSecond;

    private readonly string label;
    private readonly int fps;

    private double sinceReport;
    private double expectedSinceReport;
    private int advancedSinceReport;

    public ReplayClock(string label, int fps)
    {
      this.label = label;
      this.fps = fps < 1 ? 1 : fps;

      Logger.Info($"[Replay] {label} : cadence relue {this.fps} img/s");
    }

    /// <summary>
    /// Combien d'images consommer cette fois-ci, pour la vitesse demandee.
    ///
    /// Le reste est garde d'un appel a l'autre : a quinze images par seconde sur un
    /// ecran a soixante, on avance d'une image sur quatre, et arrondir a chaque fois
    /// donnerait zero pour toujours.
    /// </summary>
    public int Take(float rate, ref float carry)
    {
      double seconds = Engine.DeltaTicks / TicksPerSecond;

      // Une image tres longue - un chargement, une fenetre deplacee - ne doit pas
      // faire bondir la relecture d'une seconde d'un coup.
      if (seconds > 0.25)
      {
        seconds = 0.25;
      }

      carry += (float)(rate * fps * seconds);

      int take = 0;

      while (carry >= 1f)
      {
        carry -= 1f;
        take++;
      }

      Report(seconds, rate * fps * seconds, take);
      return take;
    }

    /// <summary>
    /// Ne dit quelque chose QUE si la cadence derive.
    ///
    /// Elle parlait a chaque seconde, le temps de trouver pourquoi une relecture de
    /// match defilait trop vite. La cause est connue - le mod accelerate levait
    /// <c>Engine.TimeRate</c>, dont l'ancienne formule dependait - et une ligne par
    /// seconde ne serait plus qu'un bruit qui noierait le reste du journal. Elle reste
    /// pour l'ecart qu'on n'attend pas : un disque qui ne suit pas, une base de temps
    /// qu'on aurait a nouveau mal choisie.
    /// </summary>
    private void Report(double seconds, double expected, int take)
    {
      sinceReport += seconds;
      expectedSinceReport += expected;
      advancedSinceReport += take;

      if (sinceReport < 1.0)
      {
        return;
      }

      // L'attendu suit la VITESSE choisie : a x4 on consomme quatre fois la cadence,
      // et comparer a la cadence nue ferait crier le journal a chaque acceleration
      // volontaire.
      if (Math.Abs(advancedSinceReport - expectedSinceReport) > expectedSinceReport * 0.25 + 2)
      {
        Logger.Info($"[Replay] {label} : {advancedSinceReport} img consommees en "
            + $"{sinceReport:0.00} s, {expectedSinceReport:0.0} attendues");
      }

      sinceReport = 0.0;
      expectedSinceReport = 0.0;
      advancedSinceReport = 0;
    }
  }
}
