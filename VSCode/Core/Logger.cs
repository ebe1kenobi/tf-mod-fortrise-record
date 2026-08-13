using Microsoft.Extensions.Logging;

namespace TFModFortRiseRecord
{
  /// <summary>
  /// Journal du mod.
  ///
  /// FortRise 5 fournit un ILogger par mod : ses lignes vont dans le journal du
  /// launcher (<c>FortRise/Logs</c>), datees, prefixees du niveau et du nom du mod.
  /// Le mod n'a donc plus de fichier a ouvrir, a nommer, a verrouiller ni a purger -
  /// c'etait tout le travail de CustomLogger, qui disparait avec lui. Un seul
  /// journal pour le jeu entier, dans l'ordre reel des evenements : c'est le seul
  /// moyen de voir ce que deux mods se sont fait l'un a l'autre.
  ///
  /// La classe reste, et avec elle tous les appels existants. Non initialisee, elle
  /// PERD ses messages au lieu de tomber : un journal ne doit jamais faire echouer
  /// ce qu'il journalise.
  /// </summary>
  public static class Logger
  {
    private static ILogger backend;

    /// <summary>Appele une fois par le module, avec l'ILogger qu'il a recu.</summary>
    public static void Init(ILogger modLogger)
    {
      backend = modLogger;
    }

    /// <summary>
    /// Le message part comme PARAMETRE et non comme gabarit : un texte contenant des
    /// accolades - un JSON, une position formatee - serait sinon relu comme un
    /// modele a trous et leverait au moment de l'ecriture.
    /// </summary>
    public static void Info(string message)
    {
      backend?.LogInformation("{message}", message);
    }

    public static void Error(string message)
    {
      backend?.LogError("{message}", message);
    }

    /// <summary>
    /// Garde pour les appels existants. Le niveau etait une chaine libre posee en
    /// tete de ligne ; seul "ERROR" distinguait vraiment quelque chose.
    /// </summary>
    public static void Log(string message, string level)
    {
      if (level != null && level.Trim() == "ERROR")
      {
        Error(message);
        return;
      }

      Info(message);
    }

    /// <summary>Ligne brute d'autrefois : elle part desormais en information.</summary>
    public static void WriteLine(string message)
    {
      Info(message);
    }
  }
}
