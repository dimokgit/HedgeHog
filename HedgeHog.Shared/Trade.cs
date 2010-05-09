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
  public class Trade {
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
    [DisplayName("##")]
    [DisplayFormat(DataFormatString = "{0}")]
    public TradeRemark Remark { get; set; }
    [DataMember]
    [DisplayName("")]
    public double Open { get; set; }
    [DataMember]
    [DisplayName("")]
    public double Limit { get; set; }
    [DataMember]
    public double PL { get; set; }
    [DataMember]
    [DisplayName("")]
    public double GrossPL { get; set; }
    [DataMember]
    [DisplayFormat(DataFormatString = "{0:dd HH:mm}")]
    public DateTime Time { get; set; }
    [DataMember]
    public int Lots { get; set; }

    public override string ToString() {
      var x = new XElement(GetType().Name,
      GetType().GetProperties().Select(p => new XElement(p.Name, p.GetValue(this, null) + "")));
      return x.ToString(SaveOptions.DisableFormatting);
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
      return string.Join(PIPE + "",
        new object[] {
          TradeWaveInMinutes.ToString("000"),
          TradeWaveHeight ,
          Angle
        }.Select(o => o + "").ToArray());
    }
  }
}
