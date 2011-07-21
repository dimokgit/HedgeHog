﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  public class ReplayArguments:GalaSoft.MvvmLight.ViewModelBase {
    private double _DelayInSeconds;
    public double DelayInSeconds {
      get { return _DelayInSeconds; }
      set { 
        _DelayInSeconds = value;
        RaisePropertyChanged("DelayInSeconds");
      }
    }

    private bool _InPause;
    public bool InPause {
      get { return _InPause; }
      set {
        if (_InPause != value) {
          _InPause = value;
          RaisePropertyChanged("InPause");
        }
      }
    }

    private bool _StepForward;
    public bool StepForward {
      get { return _StepForward; }
      set {
        if (_StepForward != value) {
          _StepForward = value;
          RaisePropertyChanged("StepForward");
        }
      }
    }

    private bool _StepBack;
    public bool StepBack {
      get { return _StepBack; }
      set {
        if (_StepBack != value) {
          _StepBack = value;
          RaisePropertyChanged("StepBack");
        }
      }
    }

    private bool _MustStop;
    public bool MustStop {
      get { return _MustStop; }
      set {
        if (_MustStop != value) {
          _MustStop = value;
          RaisePropertyChanged("MustStop");
        }
      }
    }


  }
}
