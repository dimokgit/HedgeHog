using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Models;

namespace HedgeHog.Alice.Client {
  public class MarketHours : ModelBase{
    #region Market
    private string _Market;
    public string Market {
      get { return _Market; }
      set {
        if (_Market != value) {
          _Market = value;
          RaisePropertyChanged("Market");
        }
      }
    }
    #endregion
    #region TimeZone
    private string _TimeZone;
    public string TimeZone {
      get { return _TimeZone; }
      set {
        if (_TimeZone != value) {
          _TimeZone = value;
          RaisePropertyChanged("TimeZone");
        }
      }
    }
    #endregion
    #region Opens
    private string _Opens;
    public string Opens {
      get { return _Opens; }
      set {
        if (_Opens != value) {
          _Opens = value;
          RaisePropertyChanged("Opens");
        }
      }
    }
    #endregion
    #region Closes
    private string _Closes;
    public string Closes {
      get { return _Closes; }
      set {
        if (_Closes != value) {
          _Closes = value;
          RaisePropertyChanged("Closes");
        }
      }
    }
    #endregion
    #region Status
    private string _Status;
    public string Status {
      get { return _Status; }
      set {
        if (_Status != value) {
          _Status = value;
          RaisePropertyChanged("Status");
        }
      }
    }
    #endregion
  }
}
