﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.ComponentModel;
using System.Xml.Linq;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

namespace HedgeHog.Shared {
  public static class TradeExtensions {
    public static double DistanceMaximum(this IList<Trade> trades) {
      return trades.DistanceMaximum(t => t.PL);
    }
    public static double DistanceMaximum(this IList<Trade> trades, Func<Trade, double> tradeValue) {
      var distanceMax = 0.0;
      if (trades != null && trades.Count > 1)
        trades.OrderBy(tradeValue).Aggregate((tp, tn) => {
          distanceMax = Math.Max(distanceMax, tradeValue(tn) - tradeValue(tp));
          return tn;
        });
      return distanceMax;
    }

    public static int Positions(this IEnumerable<Trade> trades,int lotBase) {
      return ((double)trades.Sum(t => t.Lots) / lotBase).Ceiling();
    }
    public static int Lots(this IEnumerable<Trade> trades) {
      return trades.Select(t => t.Lots).DefaultIfEmpty().Sum();
    }
    public static double NetLots(this IEnumerable<Trade> trades) {
      return trades == null || trades.Count() == 0 ? 0 : trades.Sum(t => t.Buy ? t.Lots : -t.Lots);
    }
    public static double GrossInPips(this IEnumerable<Trade> trades) {
      return trades == null || trades.Count() == 0 ? 0 : trades.Sum(t => t.PL * t.Lots) / trades.Sum(t => t.Lots);
    }
    public static double NetOpen(this IEnumerable<Trade> trades, double defaultValue = 0) {
      return trades == null || trades.Count() == 0 ? defaultValue : trades.Sum(t => t.Open * t.Lots) / trades.Sum(t => t.Lots);
    }
    public static double NetClose(this IEnumerable<Trade> trades) {
      return trades == null || trades.Count() == 0 ? 0 : trades.Sum(t => t.Close * t.Lots) / trades.Sum(t => t.Lots);
    }
    public static double Gross(this IEnumerable<Trade> trades) {
      return trades == null ? 0 : trades.Select(t => t.GrossPL).DefaultIfEmpty(0).Sum(t => t);
    }
    public static Trade[] ByPair(this ICollection<Trade> trades, string pair) {
      return (string.IsNullOrWhiteSpace(pair) ? trades : trades.Where(t => t.Pair == pair)).ToArray();
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
  public class TradeEventArgs : EventArgs {
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
  public class Trade : PositionBase {
    [DataMember]
    public string Id { get; set; }
    string _Pair;

    [DataMember]
    public string Pair {
      get { return _Pair; }
      set {
        _Pair = value;
      }
    }
    [DataMember]
    [DisplayName("BS")]
    public bool Buy { get; set; }
    [DataMember]
    [DisplayName("")]
    public bool IsBuy { get; set; }
    [DataMember]
    [DisplayName("")]
    [DisplayFormat(DataFormatString = "{0}")]
    public TradeRemark Remark { get; set; }
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
        if (BaseUnitSize == 0) return;
        var gross = Buy ? Close - Open : Open - Close;
        PL = gross / PipSize;
        var offset = Pair == "USDOLLAR" ? 1 : 10.0;
        GrossPL = gross * (Lots / (offset * BaseUnitSize)) / PipSize;
      }
    }
    [DataMember]
    [DisplayName("")]
    [UpdateOnUpdate("LimitInPips","LimitToCloseInPips")]
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
        if (_Stop != value) {
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
    [DataMember]
    [DisplayFormat(DataFormatString = "{0:dd HH:mm}")]
    public DateTime Time { get; set; }
    [DataMember]
    [DisplayName("Time Close")]
    public DateTime TimeClose { get; set; }
    public DateTime DateClose { get { return TimeClose.Date; } }
    public int DaysSinceClose { get { return Math.Floor((DateTime.Now - TimeClose).TotalDays).ToInt(); } }
    [DataMember]
    public int Lots { get; set; }
    public int AmountK { get { return Lots / (BaseUnitSize == 0 ? 1000 : BaseUnitSize); } }

    [DataMember]
    public string OpenOrderID { get; set; }
    [DataMember]
    public string OpenOrderReqID { get; set; }
    [DataMember]
    public string StopOrderID { get; set; }
    [DataMember]
    public string LimitOrderID { get; set; }
    

    [DataMember]
    public double Commission { get; set; }

    [DataMember]
    public bool IsVirtual { get; set; }

    private ITradesManager _tradesManager;

    public ITradesManager TradesManager {
      get { return _tradesManager; }
      set {
        if (_tradesManager != null)
          _tradesManager.PriceChanged -= UpdateByPrice;
        _tradesManager = value;
        if (_tradesManager != null)
          _tradesManager.PriceChanged += UpdateByPrice;
      }
    }

    public void UpdateByPrice(object sender, PriceChangedEventArgs e) {
      UpdateByPrice(sender as ITradesManager, e.Price);
    }
    public int BaseUnitSize { get; set; }
    public void UpdateByPrice(ITradesManager tradesManager, Price price) {
      if (price.Pair == Pair) {
        if (PipSize == 0) PipSize = tradesManager.GetPipSize(Pair);
        if (BaseUnitSize == 0) BaseUnitSize = tradesManager.GetBaseUnitSize(Pair);
        TimeClose = price.Time;
        Close = Buy ? price.Bid : price.Ask;
        //Close = Buy ? price.BuyClose : price.SellClose;
      }
    }

    public double NetPL { get { return GrossPL + Commission; } }
    public double OpenInPips { get { return InPips(this.Open); } }
    public double CloseInPips { get { return InPips(this.Close); } }

    public bool IsParsed { get; set; }

    /// <summary>
    /// 100,10000
    /// </summary>
    public int PipValue { get { return (int)Math.Round(Math.Abs(this.PL / (this.Open - this.Close)), 0); } }
    /// <summary>
    /// 2,4
    /// </summary>
    public int PointSize { get { return (int)Math.Log10(PipValue); } }

    public string PointSizeFormat { get { return "n" + PointSize; } }

    public double InPips(double value) { return value * PipValue; }

    public Trade Clone() { return this.MemberwiseClone() as Trade; }

    public override string ToString() { return ToString(SaveOptions.DisableFormatting); }
    public string ToString(SaveOptions saveOptions) {
      var x = new XElement(GetType().Name,
      GetType().GetProperties().Select(p => new XElement(p.Name, p.GetValue(this, null) + "")));
      return x.ToString(saveOptions);
    }
    public Trade FromString(string xmlString) {
      var x = XElement.Parse(xmlString);
      var nodes = x.Nodes().ToArray();
      foreach (var property in GetType().GetProperties()) {
        var element = x.Element(property.Name);
        if (element != null && property.CanWrite && property.PropertyType != typeof(UnKnownBase))
          if (property.PropertyType == typeof(TradeRemark))
            this.Remark = new TradeRemark(element.Value);
          else
            this.SetProperty(property.Name, element.Value);
      }
      return this;
    }
    public void FromString(XElement xmlElement) {
      this.Buy = this.IsBuy = xmlElement.Attribute("BS").Value == "B";
      this.Close = double.Parse(xmlElement.Attribute("Close").Value);
      this.GrossPL = double.Parse(xmlElement.Attribute("GrossPL").Value);
      this.Id = xmlElement.Attribute("TradeID").Value;
      this.Lots = int.Parse(xmlElement.Attribute("Lot").Value);
      this.Pair = xmlElement.Attribute("Instrument").Value;
      this.PL = double.Parse(xmlElement.Attribute("PL").Value);
      this.Time = DateTime.Parse(xmlElement.Attribute("OpenTime").Value);
      this.TimeClose = DateTime.Parse(xmlElement.Attribute("CloseTime").Value);

      this.Remark = new TradeRemark(xmlElement.Attribute("CQTXT").Value);
    }

    double _PipSize;

    [UpdateOnUpdate]
    public double PipSize {
      get { return _PipSize; }
      set {
        _PipSize = value; 
      }
    }


  }
  [Serializable]
  [DataContract]
  public class TradeRemark {
    [DataMember]
    public string Remark { get; set; }
    [DataMember]
    const char PIPE = '|';
    [DataMember]
    int _tradeWaveInMinutes = 0;
    public int TradeWaveInMinutes {
      get { return _tradeWaveInMinutes; }
      set {
        if (value < 1000)
          _tradeWaveInMinutes = value;
        else _tradeWaveInMinutes = 0;
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
    public TradeRemark(int tradeWaveInMinutes, double tradeWaveHeight, double angle) {
      TradeWaveInMinutes = tradeWaveInMinutes;
      TradeWaveHeight = Math.Round(tradeWaveHeight, 1);
      Angle = Math.Round(angle, 2);
    }
    public TradeRemark(string remark) {
      this.Remark = remark;
      var info = remark.Split(new[] { PIPE }, StringSplitOptions.RemoveEmptyEntries);
      if (info.Length > 0) int.TryParse(info[0], out _tradeWaveInMinutes);
      if (info.Length > 1) double.TryParse(info[1], out _tradeWaveHeight);
      if (info.Length > 2) double.TryParse(info[2], out _angle);
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
