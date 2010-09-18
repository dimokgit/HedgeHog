using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.ComponentModel;
using System.Xml.Linq;
using System.ComponentModel.DataAnnotations;

namespace HedgeHog.Shared {
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
  public class Trade : PositioBase {
    [DataMember]
    public string Id { get; set; }
    [DataMember]
    public string Pair { get; set; }
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
    [DataMember]
    [DisplayName("")]
    public double Open { get; set; }
    [DataMember]
    [DisplayName("")]
    [UpdateOnUpdate]
    public double Close { get; set; }
    [DataMember]
    [DisplayName("")]
    [UpdateOnUpdate("LimitInPips")]
    public double Limit { get; set; }
    [DisplayName("")]
    public double LimitInPips { get { return Limit == 0 ? 0 : InPips(IsBuy ? Limit - Open : Open - Limit); } }
    [DisplayName("")]
    [DataMember]
    [UpdateOnUpdate("StopInPips")]
    public double Stop { get; set; }
    [DisplayName("")]
    public double StopInPips { get { return Stop == 0 ? 0 : InPips(IsBuy ? Stop - Open : Open - Stop); } }
    [DataMember]
    [UpdateOnUpdate]
    public double PL { get; set; }
    [DataMember]
    [DisplayName("")]
    [UpdateOnUpdate]
    public double GrossPL { get; set; }
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
    public int AmountK { get { return Lots / 1000; } }

    [DataMember]
    public string OpenOrderID { get; set; }
    [DataMember]
    public string OpenOrderReqID { get; set; }
    [DataMember]
    public double Commission { get; set; }

    [DataMember]
    public bool IsVirtual { get; set; }

    private ITradesManager _tradesManager;

    public ITradesManager TradesManager {
      get { return _tradesManager; }
      set {
        if (_tradesManager != null)
          _tradesManager.PriceChanged -= TradesManager_PriceChanged;
        _tradesManager = value;
        if (_tradesManager != null)
          _tradesManager.PriceChanged += TradesManager_PriceChanged;
      }
    }

    void TradesManager_PriceChanged(Price Price) {
      UpdateByPrice(Price);
    }

    public void UpdateByPrice(Price Price) {
      if (Price.PipSize == 0) throw new Exception("Price.PipSize property must not be Zero.");
      Close = Buy ? Price.Bid : Price.Ask;
      var gross = Buy ? Close - Open : Open - Close;
      PL = gross / Price.PipSize;
      GrossPL = gross * Lots;
      TimeClose = Price.Time;
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
