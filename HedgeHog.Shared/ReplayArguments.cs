using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Subjects;
using ReactiveUI;

namespace HedgeHog.Shared {
  public class ReplayArguments<TTradingMacro>:GalaSoft.MvvmLight.ViewModelBase {

    public bool IsWww { get; set; }
    private string lastWwwError;
    public string LastWwwError {
      get {
        return lastWwwError;
      }

      set {
        lastWwwError = value;
        LastWwwErrorObservable.OnNext(value);
      }
    }
    public Subject<string> LastWwwErrorObservable { get; set; } = new Subject<string>();
    public ReplayArguments() {
    }

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
    public bool UseSuperSession { get; set; }
    public void ResetSuperSession() { SuperSessionId = Guid.Empty; }
    #endregion

    #region MonthsToTest
    private double _DaysToTest;
    public double DaysToTest {
      get { return _DaysToTest; }
      set {
        if (_DaysToTest != value) {
          _DaysToTest = value;
          RaisePropertyChanged("DaysToTest");
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
          _DateStart = DateTime.SpecifyKind(value.Value, DateTimeKind.Local);
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
    public TTradingMacro Initiator { get; set; }
    public List<TTradingMacro> TradingMacros = new List<TTradingMacro>();
    public void SetTradingMacros(IList<TTradingMacro> tms) {
      TradingMacros.Clear();
      TradingMacros.AddRange(tms);
      _TradingMacrosIndex = 0;
    }
    int _TradingMacrosIndex = 0;
    public bool IsMyTurn(TTradingMacro tm) {
      return TradingMacros[_TradingMacrosIndex].Equals(tm);
    }
    public TTradingMacro WhoseTurn(TTradingMacro tm) {
      return TradingMacros[_TradingMacrosIndex];
    }
    public void NextTradingMacro() {
      _TradingMacrosIndex = (_TradingMacrosIndex + 1) % TradingMacros.Count;
    }

    public Func<double> GetOriginalBalance { get; set; }

  }
}
