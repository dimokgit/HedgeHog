using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;

namespace HedgeHog.Alice.Store {
  public class TraderModelPersist :HedgeHog.Models.ModelBase {
    public static string CurrentDirectory() => System.Net.Dns.GetHostName() + "::" + Lib.CurrentDirectory.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Last();

    public TraderModelPersist() {
      _key = CurrentDirectory();
    }
    //public ObjectId _id { get; set; }
    private string _TradingMacroName;
    public string TradingMacroName {
      get { return _TradingMacroName; }
      set {
        if(_TradingMacroName != value) {
          _TradingMacroName = value;
          RaisePropertyChangedCore();
        }
      }
    }

    private int _IpPort;
    public int IpPort {
      get { return _IpPort; }
      set {
        if(_IpPort != value) {
          _IpPort = value;
          RaisePropertyChangedCore();
        }
      }
    }
    bool _IsInVirtualTrading;
    public bool IsInVirtualTrading {
      get => _IsInVirtualTrading;
      set {
        if(_IsInVirtualTrading != value) {
          _IsInVirtualTrading = value;
          RaisePropertyChangedCore();
        }
      }
    }

    string _IB_IPAddress = "127.0.0.1";
    public string IB_IPAddress {
      get => _IB_IPAddress;
      set {
        if(_IB_IPAddress != value) {
          _IB_IPAddress = value;
          RaisePropertyChangedCore();
        }
      }
    }
    int _IB_IPPort = 7497;
    public int IB_IPPort {
      get => _IB_IPPort;
      set {
        if(_IB_IPPort != value) {
          _IB_IPPort = value;
          RaisePropertyChangedCore();
        }
      }
    }
    int _IB_ClientId = 0;
    public int IB_ClientId {
      get => _IB_ClientId;
      set {
        if(_IB_ClientId != value) {
          _IB_ClientId = value;
          RaisePropertyChangedCore();
        }
      }
    }

    public double GrossToExitSave {
      get => GrossToExit < 1 ? GrossToExit : 0;
      set {
        if(GrossToExit != value) {
          GrossToExit = value;
        }
      }
    }
    double _GrossToExit = 0;
    [BsonIgnore]
    public double GrossToExit {
      get => _GrossToExit;
      set {
        if(_GrossToExit != value) {
          _GrossToExit = value;
          RaisePropertyChangedCore();
        }
      }
    }
    double _profitByHedgeRatioDiff = 1 / 3.0;
    public double ProfitByHedgeRatioDiff {
      get => _profitByHedgeRatioDiff;
      set {
        if(_profitByHedgeRatioDiff != value) {
          _profitByHedgeRatioDiff = value;
          RaisePropertyChangedCore();
        }
      }
    }

  }
}