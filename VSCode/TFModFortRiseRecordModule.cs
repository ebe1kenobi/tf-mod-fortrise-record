using System;
using System.Diagnostics;
using FortRise;
using Monocle;
using TowerFall;

namespace TFModFortRiseRecord
{
  [Fort("com.ebe1.kenobi.tfmodfortriserecord", "TFModFortRiseRecord")]
  public class TFModFortRiseRecordModule : FortModule
  {
    public static TFModFortRiseRecordModule Instance;

    public override Type SettingsType => typeof(TFModFortRiseRecordSettings);
    public static TFModFortRiseRecordSettings Settings => (TFModFortRiseRecordSettings)Instance.InternalSettings;

    public TFModFortRiseRecordModule()
    {
      if (!Debugger.IsAttached)
      {
        //Debugger.Launch();
      }
      Instance = this;
      Logger.Init("TFModFortRiseRecord");
    }

    public override void LoadContent()
    {
    }

    public override void Load()
    {
      MatchRecorder.Load();
    }

    public override void Unload()
    {
      MatchRecorder.Unload();
    }
  }
}
