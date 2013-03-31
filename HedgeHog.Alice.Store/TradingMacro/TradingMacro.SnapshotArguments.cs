using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog;
using Gala = GalaSoft.MvvmLight.Command;
using System.Windows.Input;
using System.Diagnostics;

namespace HedgeHog.Alice.Store {
  public class SnapshotArguments : GalaSoft.MvvmLight.ViewModelBase {

    #region StartDate
    private DateTime? _DateStart;
    public DateTime? DateStart {
      get { return _DateStart; }
      set {
        if (_DateStart != value) {
          _DateStart = value;
          RaisePropertyChanged(Lib.GetLambda(() => DateStart));
          if (_DateStart == null) DateEnd = null;
        }
      }
    }
    #endregion

    #region DateEnd
    private DateTime? _DateEnd;
    public DateTime? DateEnd {
      get { return _DateEnd; }
      set {
        if (_DateEnd != value) {
          _DateEnd = value;
          RaisePropertyChanged(Lib.GetLambda(() => DateEnd));
        }
      }
    }
    #endregion

    #region IsTarget
    private bool _IsTarget;
    public bool IsTarget {
      get { return _IsTarget; }
      set {
        if (_IsTarget != value) {
          _IsTarget = value;
          RaisePropertyChanged("IsTarget");
        }
      }
    }

    #endregion

    #region Label
    private string _Label = "Match";
    public string Label {
      get { return _Label; }
      set {
        if (_Label != value) {
          _Label = value;
          RaisePropertyChanged("Label");
        }
      }
    }

    #endregion
    #region ShowSnapshot Event
    event EventHandler<EventArgs> ShowSnapshotEvent;
    public event EventHandler<EventArgs> ShowSnapshot {
      add {
        if (ShowSnapshotEvent == null || !ShowSnapshotEvent.GetInvocationList().Contains(value))
          ShowSnapshotEvent += value;
      }
      remove {
        ShowSnapshotEvent -= value;
      }
    }
    protected void RaiseShowSnapshot() {
      if (ShowSnapshotEvent != null) ShowSnapshotEvent(this, new EventArgs());
    }
    #endregion

    #region Show
    ICommand _ShowCommand;
    public ICommand ShowCommand {
      get {
        if (_ShowCommand == null) {
          _ShowCommand = new Gala.RelayCommand(Show, () => true);
        }

        return _ShowCommand;
      }
    }
    void Show() {
      RaiseShowSnapshot();
    }
    #endregion

    #region Advance
    #region AdvanceSnapshot Event
    event EventHandler<EventArgs> AdvanceSnapshotEvent;
    public event EventHandler<EventArgs> AdvanceSnapshot {
      add {
        if (AdvanceSnapshotEvent == null || !AdvanceSnapshotEvent.GetInvocationList().Contains(value))
          AdvanceSnapshotEvent += value;
      }
      remove {
        AdvanceSnapshotEvent -= value;
      }
    }
    protected void RaiseAdvanceSnapshot() {
      if (AdvanceSnapshotEvent != null) AdvanceSnapshotEvent(this, new EventArgs());
    }
    #endregion

    ICommand _AdvanceCommand;
    public ICommand AdvanceCommand {
      get {
        if (_AdvanceCommand == null) {
          _AdvanceCommand = new Gala.RelayCommand(Advance, () => true);
        }

        return _AdvanceCommand;
      }
    }
    void Advance() { RaiseAdvanceSnapshot(); }
    #endregion

    #region Descend
    #region DescendSnapshot Event
    event EventHandler<EventArgs> DescendSnapshotEvent;
    public event EventHandler<EventArgs> DescendSnapshot {
      add {
        if (DescendSnapshotEvent == null || !DescendSnapshotEvent.GetInvocationList().Contains(value))
          DescendSnapshotEvent += value;
      }
      remove {
        DescendSnapshotEvent -= value;
      }
    }
    protected void RaiseDescendSnapshot() {
      if (DescendSnapshotEvent != null) DescendSnapshotEvent(this, new EventArgs());
    }
    #endregion

    ICommand _DescendCommand;
    public ICommand DescendCommand {
      get {
        if (_DescendCommand == null) {
          _DescendCommand = new Gala.RelayCommand(Descend, () => true);
        }

        return _DescendCommand;
      }
    }
    void Descend() { RaiseDescendSnapshot(); }
    #endregion


    #region Match
    #region MatchSnapshotRange Event
    event EventHandler<EventArgs> MatchSnapshotRangeEvent;
    public event EventHandler<EventArgs> MatchSnapshotRange {
      add {
        if (MatchSnapshotRangeEvent == null || !MatchSnapshotRangeEvent.GetInvocationList().Contains(value))
          MatchSnapshotRangeEvent += value;
      }
      remove {
        MatchSnapshotRangeEvent -= value;
      }
    }
    protected void RaiseMatchSnapshotRange() {
      if (MatchSnapshotRangeEvent != null) MatchSnapshotRangeEvent(this, new EventArgs());
    }
    #endregion

    ICommand _MatchCommand;
    public ICommand MatchCommand {
      get {
        if (_MatchCommand == null) {
          _MatchCommand = new Gala.RelayCommand(Match, () => true);
        }

        return _MatchCommand;
      }
    }
    void Match() { RaiseMatchSnapshotRange(); }
    #endregion

  }
}
