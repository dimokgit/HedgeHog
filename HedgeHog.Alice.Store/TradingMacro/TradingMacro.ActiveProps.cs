using HedgeHog.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data.Entity.Core.Objects.DataClasses;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    #region TradingRatio
    private global::System.Double _TradingRatio;
    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [DataMemberAttribute()]
    [WwwSetting(wwwSettingsTradingParams)]
    public global::System.Double TradingRatio {
      get {
        return _TradingRatio;
      }
      set {
        if(_TradingRatio == value) return;
        _TradingRatio = value;
        OnPropertyChanged("TradingRatio");
      }
    }
    //private string _TradingRatioEx;
    //public string TradingRatioEx {
    //  get { return _TradingRatioEx ?? ""; }
    //  set {
    //    if(_TradingRatioEx != value) {
    //      if(value.IsNullOrWhiteSpace()) {
    //        var split = TradingRatioEx.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Select(double.Parse).ToArray();
    //        TradingRatio = split.Take(1).SingleOrDefault();
    //      } else {
    //        var split = value.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Select(double.Parse).ToArray();
    //        TradingRatio = split.Take(1).SingleOrDefault();
    //        TradingRatioHedge = split.Skip(1).Take(1).SingleOrDefault();
    //      }
    //      _TradingRatioEx = value.IsNullOrWhiteSpace() ? null : value;
    //      OnPropertyChanged("TradingRatioEx");
    //    }
    //  }
    //}
    int _HedgeQuantity = 1;
    public int HedgeQuantity {
      get {
        return _HedgeQuantity;
      }
      set {
        if(_HedgeQuantity == value) return;
        _HedgeQuantity = value;
        OnPropertyChanged("HedgeQuantity");
      }
    }
    private double tradingRatioHedge;
    [WwwSetting(wwwSettingsTradingParams)]
    public double TradingRatioHedge {
      get => tradingRatioHedge;
      set {
        if(tradingRatioHedge == value) return;
        tradingRatioHedge = value;
        OnPropertyChanged("TradingRatioHedge");
      }
    }
    #endregion
    TimeSpan _timeFrameTresholdTimeSpan;
    public TimeSpan TimeFrameTresholdTimeSpan {
      get {
        return _timeFrameTresholdTimeSpan;
      }
      private set {
        _timeFrameTresholdTimeSpan = value;
        OnPropertyChanged("TimeFrameTresholdTimeSpan");
      }
    }
    TimeSpan _timeFrameTresholdTimeSpan2 = TimeSpan.Zero;
    public TimeSpan TimeFrameTresholdTimeSpan2 {
      get {
        return _timeFrameTresholdTimeSpan2;
      }
      private set {
        _timeFrameTresholdTimeSpan2 = value;
        OnPropertyChanged("TimeFrameTresholdTimeSpan2");
      }
    }
    string _timeFrameTreshold = "0:00";
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsTrading)]
    public string TimeFrameTreshold {
      get {
        return _timeFrameTreshold;
      }

      set {
        if(_timeFrameTreshold == value)
          return;
        _timeFrameTreshold = value;
        var spans = value.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
        if(spans.IsEmpty())
          spans = new[] { "0:00" };
        TimeFrameTresholdTimeSpan = TimeSpan.Parse(spans[0]);
        TimeFrameTresholdTimeSpan2 = spans.Length > 1 ? TimeSpan.Parse(spans[1]) : TimeSpan.Zero;
        OnPropertyChanged(nameof(TimeFrameTreshold));
      }
    }
    #region RatesLengthMinutesMin
    TimeSpan RatesTimeSpanMinimum => TimeSpan.Parse(RatesMinutesMin);
    private string _RatesMinutesMin = "2:00:00";
    [Category(categoryActive)]
    [WwwSetting(wwwSettingsTradingCorridor)]
    public string RatesMinutesMin {
      get { return _RatesMinutesMin; }
      set {
        if(_RatesMinutesMin != value) {
          var ts = string.IsNullOrWhiteSpace(value) ? "0:00" : value;
          TimeSpan.Parse(ts);
          _RatesMinutesMin = ts;
          OnPropertyChanged(nameof(RatesMinutesMin));
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
        if(_Rsd != value) {
          _Rsd = value;
          OnPropertyChanged("Rsd");
        }
      }
    }
    #endregion
    #region TradeCountStart
    private int _TradeCountStart;
    [WwwSetting(Group = wwwSettingsTradingConditions)]
    [Category(categoryActive)]
    [DisplayName("TradeCountStart")]
    [Description("Starting TradeCount for Buy/Sell Trade Lines")]
    public int TradeCountStart {
      get { return _TradeCountStart; }
      set {
        if(_TradeCountStart != value) {
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
        if(_PriceFftLevelsFast != value) {
          _PriceFftLevelsFast = value;
          OnPropertyChanged("PriceFftLevelsFast");
        }
      }
    }
    #endregion

    #region PriceFftLevelsSlow
    private int _PriceFftLevelsSlow = 3;
    [Category(categoryCorridor)]
    public int PriceFftLevelsSlow {
      get { return _PriceFftLevelsSlow; }
      set {
        if(_PriceFftLevelsSlow != value) {
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
        if(_VoltsHighIterations != value) {
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
        if(_VoltsFrameLength != value) {
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
        if(_CloseTradesBeforeNews != value) {
          _CloseTradesBeforeNews = value;
          OnPropertyChanged("CloseTradesBeforeNews");
        }
      }
    }

    #endregion
    TradeDirections _TradeDirection = TradeDirections.Both;
    [WwwSetting(Group = wwwSettingsTrading)]
    [Category(categoryActive)]
    [DisplayName("Trade Direction")]
    public TradeDirections TradeDirection {
      get { return _TradeDirection; }
      set {
        if(_TradeDirection != value) {
          _TradeDirection = value;
          if(BuyLevel != null && !value.HasUp() && BuyLevel.CanTrade) BuyLevel.CanTrade = false;
          if(SellLevel != null && !value.HasDown() && SellLevel.CanTrade) SellLevel.CanTrade = false;
          OnPropertyChanged("TradeDirection");
        }
      }
    }
    #region SendSMS
    private bool _sendSMS = true;

    [WwwSetting(Group = wwwSettingsTrading)]
    [Category(categoryActiveYesNo)]
    [DisplayName("Send SMS")]
    public bool SendSMS {
      get { return HedgeParent.Select(tm => tm.SendSMS).DefaultIfEmpty(_sendSMS).SingleOrDefault(); }
      set {
        if(_sendSMS != value) {
          _sendSMS = value;
          OnPropertyChanged(nameof(SendSMS));
        }
      }
    }

    #endregion
  }
}
