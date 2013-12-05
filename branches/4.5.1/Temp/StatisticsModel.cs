using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using C=System.Configuration;
using Order2GoAddIn;


namespace HedgeHog.Statistics {
  class StatisticsModel:HedgeHog.Models.ModelBase{
    private string[] _pairs;

    public string[] Pairs {
      get { return _pairs; }
      set {
        _pairs = value;
        RaisePropertyChanged(() => Pairs);
      }
    }
    FXCoreWrapper _fw;

    public FXCoreWrapper fw {
      get { return _fw; }
      set {
        if (_fw == value) return;
        _fw = value;
        _fw.CoreFX.LogOn("MICR511009001", "2293", true);
        Pairs = C.ConfigurationManager.AppSettings["Pair"].Split(',');
      }
    }
  }
}
