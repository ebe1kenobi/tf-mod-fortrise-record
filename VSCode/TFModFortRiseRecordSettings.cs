using FortRise;

namespace TFModFortRiseRecord
{
  public class TFModFortRiseRecordSettings : ModuleSettings
  {
    // PNG est sans perte : ces trois choix ne changent QUE la taille des fichiers
    // et le temps CPU du thread d'ecriture, jamais la qualite de l'image.
    private static readonly string[] PngCompressionNames = ["Fast", "Balanced", "Smallest"];
    private static readonly string[] GifQualityNames = ["High", "Medium", "Low"];

    private static string OptionName(string[] names, int index)
    {
      if (index < 0 || index >= names.Length)
        return names[0];
      return names[index];
    }

    public override void Create(ISettingsCreate settings)
    {
      // L'entree du lecteur de replays. Un bouton ici plutot qu'une lame du menu
      // principal : les lames se suivent sans interstice, en inserer une obligerait a
      // deplacer celles des autres mods - et c'est de toute facon dans les reglages du
      // mod qu'on va chercher ce qu'il sait faire.
      settings.CreateButton("Watch replays", () =>
      {
        var menu = Monocle.Engine.Instance.Scene as TowerFall.MainMenu;

        if (menu != null)
        {
          menu.State = FortRise.ModRegisters.MenuState<UIReplayList>();
        }
      });

      settings.CreateOnOff("Enable recording", recordEnabled, (x) => recordEnabled = x);
      settings.CreateOnOff("Record versus matches", recordVersus, (x) => recordVersus = x);
      settings.CreateOnOff("Record co-op (quest / dark world)", recordCoop, (x) => recordCoop = x);
      settings.CreateNumber("Captures per second", recordFps, (x) => recordFps = x, 1, 60);
      settings.CreateOnOff("Record images (PNG)", recordImages, (x) => recordImages = x);
      settings.CreateOptions("PNG compression", OptionName(PngCompressionNames, recordPngCompression), PngCompressionNames, (x) => recordPngCompression = x.Item2);
      settings.CreateOnOff("Record player inputs", recordInputs, (x) => recordInputs = x);
      settings.CreateOnOff("Record game state (positions/grid)", recordState, (x) => recordState = x);
      settings.CreateOnOff("Make GIF per round", recordGif, (x) => recordGif = x);
      settings.CreateOptions("GIF quality", OptionName(GifQualityNames, recordGifQuality), GifQualityNames, (x) => recordGifQuality = x.Item2);
    }

    // Interrupteur maitre : rien n'est enregistre si desactive.
    //[SettingsName("Enable recording")]
    public bool recordEnabled { get; set; } = false;

    // Les matchs versus : Last Man Standing, Headhunters, Team Deathmatch.
    public bool recordVersus { get; set; } = true;

    // Les modes a un ou plusieurs joueurs contre le jeu : Quest et Dark World.
    // Leur decoupage en rounds n'est pas le meme (voir MatchRecorder) - un round
    // y est un NIVEAU, et le jeu n'appelle jamais Session.EndRound.
    public bool recordCoop { get; set; } = true;

    // Nombre de captures par seconde (commun aux images, inputs et etat).
    //[SettingsName("Captures per second")]
    //[SettingsNumber(1, 60)]
    public int recordFps { get; set; } = 15;

    // Phase 1 : sequence d'images PNG (320x240).
    //[SettingsName("Record images (PNG)")]
    public bool recordImages { get; set; } = true;

    // Compromis taille/CPU du PNG (image identique dans les trois cas).
    // Mesure sur 27 frames reelles, par image : Fast ~60 Ko / 3 ms,
    // Balanced ~40 Ko / 6 ms, Smallest ~36 Ko / 20 ms.
    // Balanced par defaut : moitie moins de disque que Fast en restant tres
    // en dessous du budget du thread d'ecriture, meme a 60 captures/s (16 ms).
    // Smallest ne tient plus ce budget au-dela de ~45 captures/s.
    public const int CompressionFast = 0;
    public const int CompressionBalanced = 1;
    public const int CompressionSmallest = 2;
    public int recordPngCompression { get; set; } = CompressionBalanced;

    // Phase 2 : inputs de chaque joueur par frame (inputs.jsonl).
    //[SettingsName("Record player inputs")]
    public bool recordInputs { get; set; } = true;

    // Phase 2 : etat tabulaire par frame + grille de niveau (state.jsonl + level.json).
    //[SettingsName("Record game state (positions/grid)")]
    public bool recordState { get; set; } = true;

    // Assemble un GIF par round a la fin de celui-ci, dans Recordings/gif/.
    // Desactive par defaut : necessite les images, coute quelques secondes de CPU
    // en fin de round (sur le thread de fond), et tools/make_gif.py fait la meme
    // chose a posteriori en permettant d'essayer plusieurs reglages sans rejouer.
    public bool recordGif { get; set; } = false;

    // Compromis taille/fidelite du GIF, identique aux profils de make_gif.py.
    // Le levier utile est la cadence, pas la palette : sur un round reel de 277
    // images, 256 -> 48 couleurs ne gagne que 18 %, une image sur deux en gagne 42 %.
    public const int GifHigh = 0;
    public const int GifMedium = 1;
    public const int GifLow = 2;
    public int recordGifQuality { get; set; } = GifHigh;

    // (couleurs, 1 image conservee sur N) pour le profil choisi.
    public void GetGifProfile(out int colors, out int every)
    {
      switch (recordGifQuality)
      {
        case GifMedium: colors = 128; every = 2; break;
        case GifLow: colors = 48; every = 3; break;
        default: colors = 256; every = 1; break;
      }
    }
  }
}
