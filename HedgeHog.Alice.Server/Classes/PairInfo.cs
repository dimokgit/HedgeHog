using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Alice.Server {
  public class PairInfo : GalaSoft.MvvmLight.ViewModelBase {
    public string Pair { get; set; }
    private DateTime _LastDate;
    public DateTime LastDate {
      get { return _LastDate; }
      set {
        if (_LastDate != value) {
          _LastDate = value;
          RaisePropertyChanged("LastDate");
        }
      }
    }
    private int _Count;
    public int Count {
      get { return _Count; }
      set {
        if (_Count != value) {
          _Count = value;
          RaisePropertyChanged("Count");
        }
      }
    }
    private int _PullsCount;
    public int PullsCount {
      get { return _PullsCount; }
      set {
        if (_PullsCount != value) {
          _PullsCount = value;
          RaisePropertyChanged("PullsCount");
        }
      }
    }
    public PairInfo(string pair) {
      this.Pair = pair;
    }
  }
}
