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
  [Serializable]
  [DataContract]
  public class Trade : PositioBase {
    [DataMember]
    [DisplayName("")]
    public string Id { get; set; }
    [DataMember]
    [DisplayName("")]
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
    [DisplayName("")]
    public DateTime TimeClose { get; set; }
    [DataMember]
    public int Lots { get; set; }

    [DataMember]
    public string OpenOrderID { get; set; }
    [DataMember]
    public string OpenOrderReqID { get; set; }

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
    public void FromString(XElement xmlElement) {
      this.Buy = this.IsBuy = xmlElement.Attribute("BS").Value == "B";
      this.Close = double.Parse(xmlElement.Attribute("Close").Value);
      this.GrossPL = double.Parse(xmlElement.Attribute("GrossPL").Value);
      this.Id= xmlElement.Attribute("TradeID").Value;
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
      return !(Remark ?? "").Contains('|')?Remark: string.Join(PIPE + "",
        new object[] {
          TradeWaveInMinutes.ToString("000"),
          TradeWaveHeight ,
          Angle
        }.Select(o => o + "").ToArray());
    }
  }
}
