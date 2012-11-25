using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  public class ReplayArguments:GalaSoft.MvvmLight.ViewModelBase {

    #region Session Statistics
    public class SessionStatistics {
      public double ProfitToLossRatio { get; set; }

    }
    SessionStatistics _sessionStats = new SessionStatistics();
    public SessionStatistics SessionStats {
      get { return _sessionStats; }
      set { _sessionStats = value; }
    }
    #endregion

    #region SuperSession
    public Guid SuperSessionId { get; set; }
    public bool HasSuperSession { get { return SuperSessionId != Guid.Empty; } }
    public void ResetSuperSession() { SuperSessionId = Guid.Empty; }
    #endregion

    #region CurrentPosition
    private int _CurrentPosition;
    public int CurrentPosition {
      get { return _CurrentPosition; }
      set {
        if (_CurrentPosition != value) {
          _CurrentPosition = value;
          RaisePropertyChanged("CurrentPosition");
        }
      }
    }
    #endregion

    #region MonthsToTest
    private double _MonthsToTest;
    public double MonthsToTest {
      get { return _MonthsToTest; }
      set {
        if (_MonthsToTest != value) {
          _MonthsToTest = value;
          RaisePropertyChanged("MonthsToTest");
        }
      }
    }

    #endregion

    #region StartDate
    private DateTime? _DateStart;
    public DateTime? DateStart {
      get { return _DateStart; }
      set {
        if (_DateStart != value) {
          _DateStart = value;
          RaisePropertyChanged("DateStart");
        }
      }
    }
    #endregion

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
    public List<object> TradingMacros = new List<object>();
    public void SetTradingMacros<T>(IList<T> tms) {
      TradingMacros.Clear();
      TradingMacros.AddRange(tms.Cast<object>());
      _TradingMacrosIndex = 0;
    }
    int _TradingMacrosIndex = 0;
    public bool IsMyTurn(object tm) {
      return TradingMacros[_TradingMacrosIndex] == tm;
    }
    public void NextTradingMacro() {
      _TradingMacrosIndex = (_TradingMacrosIndex + 1) % TradingMacros.Count;
    }
  }
}
