using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    #region CorridorLengthRatio
    private double _CorridorLengthRatio;
    [Category(categoryActive)]
    [DisplayName("Corridor Length Ratio")]
    [Description("CanTrade = CorridorLength > RatesArrayLength * X")]
    public double CorridorLengthRatio {
      get { return _CorridorLengthRatio; }
      set {
        if (_CorridorLengthRatio != value) {
          _CorridorLengthRatio = value;
          OnPropertyChanged("CorridorLengthRatio");
        }
      }
    }
    #endregion
    #region Rsd
    private double _Rsd;
    [Category(categoryActive)]
    [DisplayName("Rsd Treshold")]
    public double RsdTreshold {
      get { return _Rsd; }
      set {
        if (_Rsd != value) {
          _Rsd = value;
          OnPropertyChanged("Rsd");
        }
      }
    }
    #endregion
    #region TradeCountStart
    private int _TradeCountStart;
    [Category(categoryActive)]
    [DisplayName("TradeCountStart")]
    [Description("Starting TradeCount for Buy/Sell Trade Lines")]
    public int TradeCountStart {
      get { return _TradeCountStart; }
      set {
        if (_TradeCountStart != value) {
          _TradeCountStart = value;
          OnPropertyChanged("TradeCountStart");
        }
      }
    }
    #endregion
  }
}
