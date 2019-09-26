using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.ComponentModel;
using System.Xml.Linq;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using MongoDB.Bson.Serialization.Attributes;

namespace HedgeHog.Shared {
  public static class TradeExtensions {
    public static int Positions(this IEnumerable<Trade> trades, int lotBase) {
      return ((double)trades.Sum(t => t.Lots) / lotBase).Ceiling();
    }
    public static int Lots(this IEnumerable<Trade> trades, Func<Trade, bool> predicate) {
      return trades.Where(predicate).Lots();
    }
    public static int Lots(this IEnumerable<Trade> trades) {
      return trades.Select(t => t.Lots).DefaultIfEmpty().Sum();
    }
    public static int NetLots(this IEnumerable<Trade> trades) {
      return trades == null || trades.Count() == 0 ? 0 : trades.Sum(t => t.Buy ? t.Lots : -t.Lots);
    }
    public static double GrossInPips(this IEnumerable<Trade> trades) {
      return trades == null || trades.Count() == 0 ? 0 : trades.Sum(t => t.PL * t.Lots) / trades.Lots();
    }
    public static double NetOpen(this IEnumerable<Trade> trades, double defaultValue = 0) {
      return trades == null || trades.Count() == 0 ? defaultValue : trades.Sum(t => t.Open * t.Lots) / trades.Sum(t => t.Lots);
    }
    public static double NetClose(this IEnumerable<Trade> trades) {
      return trades == null || trades.Count() == 0 ? 0 : trades.Sum(t => t.Close * t.Lots) / trades.Sum(t => t.Lots);
    }
    public static double Net(this IEnumerable<Trade> trades) {
      return trades == null ? 0 : trades.Select(t => t.NetPL).DefaultIfEmpty(0).Sum(t => t);
    }
    public static double Net2(this IEnumerable<Trade> trades) {
      return trades == null ? 0 : trades.Select(t => t.NetPL2).DefaultIfEmpty(0).Sum(t => t);
    }
    public static double Gross(this IEnumerable<Trade> trades) {
      return trades == null ? 0 : trades.Select(t => t.GrossPL).DefaultIfEmpty(0).Sum(t => t);
    }
    public static Trade[] ByPair(this ICollection<Trade> trades, string pair) {
      return (string.IsNullOrWhiteSpace(pair) ? trades : trades.Where(t => t.Pair.ToLower() == pair.ToLower())).ToArray();
    }
    public static bool HaveBuy(this ICollection<Trade> trades) {
      return trades.Any(t => t.Buy);
    }
    public static bool HaveSell(this ICollection<Trade> trades) {
      return trades.Any(t => !t.Buy);
    }
    public static Trade[] IsBuy(this ICollection<Trade> trades, bool isBuy) {
      return trades.Where(t => t.Buy == isBuy).ToArray();
    }
    public static Trade LastTrade(this ICollection<Trade> trades) {
      return trades.OrderByDescending(t => t.Time).FirstOrDefault();
    }
  }
  public class TradeEventArgs :EventArgs {
    public bool IsHandled { get; set; }
    public Trade Trade { get; set; }
    public TradeEventArgs(Trade newTrade) {
      this.Trade = newTrade;
    }
  }
  public delegate void TradeAddedEventHandler(Trade trade);
  public delegate void TradeRemovedEventHandler(Trade trade);
  public delegate void OrderRemovedEventHandler(Order order);
  [Serializable]
  [DataContract]
  [BsonIgnoreExtraElements]
  public class Trade :PositionBase {
    /// <summary>
    /// Not Implemented exception
    /// </summary>
    public static Func<double> PipRateNI = () => { throw new NotImplementedException(); };
    public static Trade Create(ITradesManager tradesManager, string pair, double pipSize, int baseUnitSize, Func<Trade, double> commissionByTrade) {
      return new Trade() { Pair = pair, PipSize = pipSize, BaseUnitSize = baseUnitSize, CommissionByTrade = commissionByTrade, TradesManager = tradesManager };
    }
    private Trade() {
    }

    [DataMember]
    [DisplayName("BS")]
    public bool Buy { get; set; }
    [DataMember]
    [DisplayName("")]
    public bool IsBuy { get; set; }
    //[DataMember]
    //[DisplayName("")]
    //[DisplayFormat(DataFormatString = "{0}")]
    //public TradeRemark Remark { get; set; }
    double _Open;
    [DataMember]
    [DisplayName("")]
    public double Open {
      get { return _Open; }
      set {
        _Open = value;
      }
    }
    double _Close;
    [DataMember]
    [DisplayName("")]
    [UpdateOnUpdate]
    public double Close {
      get { return _Close; }
      set {
        _Close = value;
        if(BaseUnitSize == 0)
          return;
        if(TradesManager != null)
          GrossPL = CalcGrossPL(Close);
        else {
          Debug.WriteLine("Closed Trade");
        }
      }
    }
    public double CalcGrossPL(double close) {
      var gross = Buy ? close - Open : Open - close;
      PL = gross / PipSize;
      var offset = Pair == "USDOLLAR" ? 1 : 10.0;
      return TradesManagerStatic.PipsAndLotToMoney(Pair, PL, Lots, close, PipSize); TradesManager.GetContractSize(Pair);

    }
    [DataMember]
    [DisplayName("")]
    [UpdateOnUpdate("LimitInPips", "LimitToCloseInPips")]
    public double Limit { get; set; }
    [DisplayName("")]
    public double LimitInPips { get { return Limit == 0 ? 0 : InPips(IsBuy ? Limit - Open : Open - Limit); } }
    [DisplayName("")]
    public double LimitToCloseInPips { get { return Limit == 0 ? 0 : InPips(IsBuy ? Limit - Close : Close - Limit); } }
    #region Stop
    private double _Stop;
    [DisplayName("")]
    [DataMember]
    [UpdateOnUpdate("StopInPips", "StopToCloseInPips")]
    public double Stop {
      get { return _Stop; }
      set {
        if(_Stop != value) {
          _Stop = value;
          OnPropertyChanged("Stop");
        }
      }
    }

    #endregion
    [DisplayName("")]
    public double StopInPips { get { return Stop == 0 ? 0 : InPips(IsBuy ? Stop - Open : Open - Stop); } }
    [DisplayName("")]
    public double StopToCloseInPips { get { return Stop == 0 ? 0 : InPips(IsBuy ? Stop - Close : Close - Stop); } }
    [DataMember]
    [UpdateOnUpdate]
    public double PL { get; set; }

    double _GrossPL;
    [DataMember]
    [DisplayName("")]
    [UpdateOnUpdate]
    public double GrossPL {
      get { return _GrossPL; }
      set {
        _GrossPL = value;
      }
    }
    private DateTime _time2;
    [DataMember]
    [DisplayFormat(DataFormatString = "{0:dd HH:mm}")]
    public DateTime Time2 {
      get { return _time2; }
      set {
        if(value.Kind == DateTimeKind.Unspecified)
          throw new ArgumentException(new { Time2 = new { value.Kind } } + "");
        _time2 = value;
        Time = _time2.Kind != DateTimeKind.Local ? TimeZoneInfo.ConvertTimeFromUtc(_time2, TimeZoneInfo.Local) : _time2;
      }
    }
    public void SetTime(DateTime time) {
      Time = time;
    }
    private DateTime _time2Close;
    [DataMember]
    [DisplayFormat(DataFormatString = "{0:dd HH:mm}")]
    public DateTime Time2Close {
      get { return _time2Close; }
      set {
        if(value.Kind == DateTimeKind.Unspecified)
          throw new ArgumentException(new { Time2Close = new { value.Kind } } + "");
        _time2Close = value;
        TimeClose = _time2Close.Kind != DateTimeKind.Local ? TimeZoneInfo.ConvertTimeFromUtc(_time2Close, TimeZoneInfo.Local) : _time2Close;
      }
    }
    DateTime _TimeClose;
    [DisplayFormat(DataFormatString = "{0:dd HH:mm}")]
    [DisplayName("Time Close")]
    [BsonIgnore]
    public DateTime TimeClose {
      get { return _TimeClose; }
      set { _TimeClose = value; }
    }
    public DateTime DateClose { get { return TimeClose.Date; } }
    public int DaysSinceClose { get { return Math.Floor((DateTime.Now - TimeClose).TotalDays).ToInt(); } }
    [DataMember]
    public int Lots {
      get => _lots;
      set {
        if(_lots == value) return;
        _lots = value;
        if(Lots == 0)
          LogMessage.Send(new { Trade = new { Pair, Lots } } + "");
        OnPropertyChanged(nameof(Lots));
      }
    }
    public double Position => IsBuy ? Lots : -Lots;
    public int AmountK { get { return Lots / (BaseUnitSize == 0 ? 1000 : BaseUnitSize); } }

    [DataMember]
    public string OpenOrderID { get; set; }
    [DataMember]
    public string OpenOrderReqID { get; set; }
    [DataMember]
    public string StopOrderID { get; set; }
    [DataMember]
    public string LimitOrderID { get; set; }


    double _commission = double.NaN;
    [DataMember]
    public double Commission {
      get {
        return double.IsNaN(_commission) ? (CommissionByTrade?.Invoke(this) ?? 0) : _commission;
      }
      set { _commission = value; }
    }

    [DataMember]
    public bool IsVirtual { get; set; }

    private ITradesManager _tradesManager;
    public bool IsClosed() => Kind == PositionKind.Closed;
    public void CloseTrade() {
      Kind = PositionKind.Closed;
      TradesManager = null;
    }

    [BsonIgnore]
    protected ITradesManager TradesManager {
      get { return _tradesManager; }
      set {
        if(_tradesManager != null)
          _tradesManager.PriceChanged -= UpdateByPrice;
        _tradesManager = value;
        if(_tradesManager != null) {
          _tradesManager.PriceChanged += UpdateByPrice;
          UpdateByPrice(_tradesManager);
        }
      }
    }
    //var buffer = new BroadcastBlock<Action>(n => n, new DataflowBlockOptions() { BoundedCapacity = boundedCapacity });

    public void UpdateByPrice(object sender, PriceChangedEventArgs e) {
      UpdateByPrice(sender as ITradesManager, e.Price);
    }
    public int BaseUnitSize { get; private set; }
    public void UpdateByPrice(ITradesManager tradesManager) {
      if(!tradesManager.TryGetPrice(Pair, out var price)) return;
      UpdateByPrice(tradesManager, price);
    }
    public void UpdateByPrice(ITradesManager tradesManager, Price price) {
      if(price == null)
        throw new NullReferenceException(new { price } + "");
      if(price.Pair == Pair) {
        if(PipSize == 0)
          PipSize = tradesManager.GetPipSize(Pair);
        if(BaseUnitSize == 0)
          BaseUnitSize = tradesManager.GetBaseUnitSize(Pair);
        Time2Close = price.Time2;
        Close = (Buy ? price.Bid : price.Ask) * BaseUnitSize;
        if(CommissionByTrade != null)
          Commission = CommissionByTrade(this);
        //Close = Buy ? price.BuyClose : price.SellClose;
      }
    }
    Func<Trade, double> _commissionByTrade;
    private int _lots;

    [BsonIgnore]
    public Func<Trade, double> CommissionByTrade {
      get { return _commissionByTrade; }
      set { _commissionByTrade = value; }
    }
    public double NetPL => GrossPL - (CommissionByTrade == null ? Commission : CommissionByTrade(this));
    public double NetPL2 => GrossPL - (CommissionByTrade == null ? Commission : CommissionByTrade(this) * 2);
    public double CalcNetPL2(double close) => CalcGrossPL(close) - (CommissionByTrade == null ? Commission : CommissionByTrade(this) * 2);

    public double NetPLInPips { get { return InPips(NetPL); } }
    public double OpenPrice => Open / BaseUnitSize;
    public double CloseInPips { get { return InPips(this.Close); } }

    public bool IsParsed { get; set; }

    /// <summary>
    /// 100,10000
    /// </summary>
    public int PipValue { get { return (int)Math.Round(Math.Abs(this.PL / (this.Open - this.Close)), 0); } }

    public double InPips(double value) { return value * PipValue; }

    public Trade Clone() {
      var t = this.MemberwiseClone() as Trade;
      t.TradesManager = TradesManager;
      return t;
    }

    public override string ToString() { return ToString(SaveOptions.DisableFormatting); }
    public string ToString(SaveOptions saveOptions) {
      var x = new XElement(GetType().Name,
      GetType().GetProperties().Select(p => new XElement(p.Name, p.GetValue(this, null) + "")));
      return x.ToString(saveOptions);
    }
    public Trade FromString(string xmlString) {
      var x = XElement.Parse(xmlString);
      var nodes = x.Nodes().ToArray();
      foreach(var property in GetType().GetProperties()) {
        var element = x.Element(property.Name);
        if(element != null && property.CanWrite && property.PropertyType != typeof(UnKnownBase))
          this.SetProperty(property.Name, element.Value);
      }
      return this;
    }
  }
  [Serializable]
  [DataContract]
  public class TradeRemark_Old {
    [DataMember]
    public string Remark { get; set; }
    [DataMember]
    const char PIPE = '|';
    [DataMember]
    int _tradeWaveInMinutes = 0;
    public int TradeWaveInMinutes {
      get { return _tradeWaveInMinutes; }
      set {
        if(value < 1000)
          _tradeWaveInMinutes = value;
        else
          _tradeWaveInMinutes = 0;
      }
    }
    [DataMember]
    double _tradeWaveHeight = 0;
    public double TradeWaveHeight {
      get { return _tradeWaveHeight; }
      set { _tradeWaveHeight = value; }
    }
    [DataMember]
    double _angle = 0;
    public double Angle {
      get { return _angle; }
      set { _angle = value; }
    }
    public TradeRemark_Old(int tradeWaveInMinutes, double tradeWaveHeight, double angle) {
      TradeWaveInMinutes = tradeWaveInMinutes;
      TradeWaveHeight = Math.Round(tradeWaveHeight, 1);
      Angle = Math.Round(angle, 2);
    }
    public TradeRemark_Old(string remark) {
      this.Remark = remark;
      var info = remark.Split(new[] { PIPE }, StringSplitOptions.RemoveEmptyEntries);
      if(info.Length > 0)
        int.TryParse(info[0], out _tradeWaveInMinutes);
      if(info.Length > 1)
        double.TryParse(info[1], out _tradeWaveHeight);
      if(info.Length > 2)
        double.TryParse(info[2], out _angle);
    }
    public override string ToString() {
      return !(Remark ?? "").Contains('|') ? Remark : string.Join(PIPE + "",
        new object[] {
          TradeWaveInMinutes.ToString("000"),
          TradeWaveHeight ,
          Angle
        }.Select(o => o + "").ToArray());
    }
  }
}
