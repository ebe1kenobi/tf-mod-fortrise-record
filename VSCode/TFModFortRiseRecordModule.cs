using System;
using System.Diagnostics;
using System.IO;
using FortRise;
using Microsoft.Extensions.Logging;

namespace TFModFortRiseRecord
{
  public class TFModFortRiseRecordModule : Mod
  {
    public static TFModFortRiseRecordModule Instance;

    internal Type[] Hookables = [
        typeof(MatchRecorder),
    ];

    public static TFModFortRiseRecordSettings Settings => Instance.GetSettings<TFModFortRiseRecordSettings>()!;

    // Racine des donnees du mod (Saves/<nom du mod>/) : logs ET enregistrements.
    // FortRise 5 vit hors du repertoire de TowerFall.
    public static string SavePath => Path.Combine(ModIO.GetRootPath(), "Saves", Instance.Meta.Name);

    // Un sous-dossier par match : Saves/<mod>/Recordings/match_<horodatage>/
    public static string RecordingsPath => Path.Combine(SavePath, "Recordings");

    public TFModFortRiseRecordModule(IModContent content, IModuleContext context, ILogger logger) : base(content, context, logger)
    {
      if (!Debugger.IsAttached)
      {
        //Debugger.Launch();
      }
      Instance = this;
      TFModFortRiseRecord.Logger.Init(SavePath);

      foreach (var hookable in Hookables)
      {
        hookable.GetMethod(nameof(IHookable.Load))!.Invoke(null, [context.Harmony]);
      }
    }

    public override ModuleSettings CreateSettings()
    {
      return new TFModFortRiseRecordSettings();
    }
  }
}
