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
    [Category(categoryXXX)]
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
    #region PriceFftLevelsFast
    private int _PriceFftLevelsFast = 1;
    [Category(categoryCorridor)]
    public int PriceFftLevelsFast {
      get { return _PriceFftLevelsFast; }
      set {
        if (_PriceFftLevelsFast != value) {
          _PriceFftLevelsFast = value;
          OnPropertyChanged("PriceFftLevelsFast");
        }
      }
    }
    #endregion

    #region PriceFftLevelsSlow
    private int _PriceFftLevelsSlow = 1;
    [Category(categoryCorridor)]
    public int PriceFftLevelsSlow {
      get { return _PriceFftLevelsSlow; }
      set {
        if (_PriceFftLevelsSlow != value) {
          _PriceFftLevelsSlow = value;
          OnPropertyChanged("PriceFftLevelsSlow");
        }
      }
    }

    #endregion
    #region VoltsHighIterations
    private int _VoltsHighIterations;
    [Category(categoryXXX)]
    public int VoltsHighIterations {
      get { return _VoltsHighIterations; }
      set {
        if (_VoltsHighIterations != value) {
          _VoltsHighIterations = value;
          OnPropertyChanged("VoltsHighIterations");
        }
      }
    }
    #endregion
    #region VoltsFrameLength
    private int _VoltsFrameLength;
    [Category(categoryXXX)]
    public int VoltsFrameLength {
      get { return _VoltsFrameLength; }
      set {
        if (_VoltsFrameLength != value) {
          _VoltsFrameLength = value;
          OnPropertyChanged("VoltsFrameLength");
        }
      }
    }

    #endregion
    #region CloseTradesBeforeNews
    private bool _CloseTradesBeforeNews = true;
    [Category(categoryActiveYesNo)]
    public bool CloseTradesBeforeNews {
      get { return _CloseTradesBeforeNews; }
      set {
        if (_CloseTradesBeforeNews != value) {
          _CloseTradesBeforeNews = value;
          OnPropertyChanged("CloseTradesBeforeNews");
        }
      }
    }

    #endregion
    #region TpsMin
    private double _TpsMin;
    [Category(categoryActive)]
    [DisplayName("TicksPerSec Min")]
    public double TpsMin {
      get { return _TpsMin; }
      set {
        if (_TpsMin != value) {
          _TpsMin = value;
          OnPropertyChanged("TpsMin");
        }
      }
    }

    #endregion
  }
}
