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
        typeof(MyPauseMenu),
        typeof(MyRoundResults),
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
      TFModFortRiseRecord.Logger.Init(logger);

      foreach (var hookable in Hookables)
      {
        hookable.GetMethod(nameof(IHookable.Load))!.Invoke(null, [context.Harmony]);
      }

      // Les enregistrements d'avant sont a plat : on les range par jour, une fois.
      MatchRecorder.MigrateToDayFolders();

      // Les trois ecrans du lecteur. FortRise attribue une valeur de MenuState a
      // chacun ; ModRegisters.MenuState<T>() la relit a chaque besoin plutot que de
      // la memoriser, l'ordre d'attribution n'etant pas garanti.
      context.Registry.MenuStates.RegisterMenuState("ReplayList",
          new MenuStateConfiguration { MenuStateType = typeof(UIReplayList) });
      context.Registry.MenuStates.RegisterMenuState("ReplayDay",
          new MenuStateConfiguration { MenuStateType = typeof(UIReplayDay) });
      context.Registry.MenuStates.RegisterMenuState("ReplayPlayer",
          new MenuStateConfiguration { MenuStateType = typeof(UIReplayPlayer) });
    }

    public override ModuleSettings CreateSettings()
    {
      return new TFModFortRiseRecordSettings();
    }
  }
}
