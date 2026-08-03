using FortRise;

namespace TFModFortRiseRecord
{
  public class TFModFortRiseRecordSettings : ModuleSettings
  {
    // Interrupteur maitre : rien n'est enregistre si desactive.
    [SettingsName("Enable recording")]
    public bool recordEnabled = false;

    // Nombre de captures par seconde (commun aux images, inputs et etat).
    // Plus bas = moins gourmand (moins de fichiers, moins d'encodage).
    [SettingsName("Captures per second")]
    [SettingsNumber(1, 60)]
    public int recordFps = 15;

    // Phase 1 : sequence d'images PNG (320x240).
    [SettingsName("Record images (PNG)")]
    public bool recordImages = true;

    // Phase 2 : inputs de chaque joueur par frame (inputs.jsonl).
    [SettingsName("Record player inputs")]
    public bool recordInputs = true;

    // Phase 2 : etat tabulaire par frame (positions joueurs/fleches) + grille de
    // niveau (state.jsonl + level.json).
    [SettingsName("Record game state (positions/grid)")]
    public bool recordState = true;
  }
}
