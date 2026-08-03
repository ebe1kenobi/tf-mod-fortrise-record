using FortRise;

namespace TFModFortRiseRecord
{
  public class TFModFortRiseRecordSettings : ModuleSettings
  {
    public override void Create(ISettingsCreate settings)
    {
      settings.CreateOnOff("Enable recording", recordEnabled, (x) => recordEnabled = x);
      settings.CreateNumber("Captures per second", recordFps, (x) => recordFps = x, 1, 60);
      settings.CreateOnOff("Record images (PNG)", recordImages, (x) => recordImages = x);
      settings.CreateOnOff("Record player inputs", recordInputs, (x) => recordInputs = x);
      settings.CreateOnOff("Record game state (positions/grid)", recordState, (x) => recordState = x);
    }

    // Interrupteur maitre : rien n'est enregistre si desactive.
    //[SettingsName("Enable recording")]
    public bool recordEnabled { get; set; } = false;

    // Nombre de captures par seconde (commun aux images, inputs et etat).
    //[SettingsName("Captures per second")]
    //[SettingsNumber(1, 60)]
    public int recordFps { get; set; } = 15;

    // Phase 1 : sequence d'images PNG (320x240).
    //[SettingsName("Record images (PNG)")]
    public bool recordImages { get; set; } = true;

    // Phase 2 : inputs de chaque joueur par frame (inputs.jsonl).
    //[SettingsName("Record player inputs")]
    public bool recordInputs { get; set; } = true;

    // Phase 2 : etat tabulaire par frame + grille de niveau (state.jsonl + level.json).
    //[SettingsName("Record game state (positions/grid)")]
    public bool recordState { get; set; } = true;
  }
}
