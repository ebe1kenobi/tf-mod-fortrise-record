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

    // Racine des donnees du mod (Saves/<nom du mod>/) pour les logs. FortRise 5 vit
    // hors du repertoire de TowerFall. (Les enregistrements eux-memes vont dans
    // Documents/TowerFall/Recordings, cf. MatchRecorder.)
    public static string SavePath => Path.Combine(ModIO.GetRootPath(), "Saves", Instance.Meta.Name);

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
