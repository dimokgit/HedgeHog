using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using System.Xml.Linq;
using System.ComponentModel;

namespace HedgeHog.Shared {
  [DataContract]
  public class Order : PositioBase {
    [DataMember]
    public String OrderID { get; set; }
    [DataMember]
    public String RequestID { get; set; }
    [DataMember]
    public String AccountID { get; set; }
    [DataMember]
    public String AccountName { get; set; }
    [DataMember]
    public String OfferID { get; set; }
    [DataMember]
    public String Pair { get; set; }
    [DataMember]
    public String TradeID { get; set; }
    [DataMember]
    public Boolean NetQuantity { get; set; }
    [DataMember]
    public String BS { get; set; }
    [DataMember]
    public String Stage { get; set; }
    [DataMember]
    public int Side { get; set; }
    [DataMember]
    public String Type { get; set; }
    [DataMember]
    public String FixStatus { get; set; }
    [DataMember]
    public String Status { get; set; }
    [DataMember]
    public int StatusCode { get; set; }
    [DataMember]
    public String StatusCaption { get; set; }
    [DataMember]
    public int Lot { get; set; }
    [DataMember]
    public Double AmountK { get; set; }
    [DataMember]
    public Double Rate { get; set; }
    [DataMember]
    public Double SellRate { get; set; }
    [DataMember]
    public Double BuyRate { get; set; }
    [DataMember]
    public Double Stop { get; set; }
    [DataMember]
    public Double UntTrlMove { get; set; }
    [DataMember]
    public Double Limit { get; set; }
    [DataMember]
    public DateTime Time { get; set; }
    [DataMember]
    public Boolean IsBuy { get; set; }
    [DataMember]
    public Boolean IsConditionalOrder { get; set; }
    [DataMember]
    public Boolean IsEntryOrder { get; set; }
    [DataMember]
    public int Lifetime { get; set; }
    [DataMember]
    public String AtMarket { get; set; }
    [DataMember]
    public int TrlMinMove { get; set; }
    [DataMember]
    public Double TrlRate { get; set; }
    [DataMember]
    public int Distance { get; set; }
    [DataMember]
    public String GTC { get; set; }
    [DataMember]
    public String Kind { get; set; }
    [DataMember]
    public String QTXT { get; set; }
    [DataMember]
    public String StopOrderID { get; set; }
    [DataMember]
    public String LimitOrderID { get; set; }
    [DataMember]
    public int TypeSL { get; set; }
    [DataMember]
    public int TypeStop { get; set; }
    [DataMember]
    public int TypeLimit { get; set; }
    [DataMember]
    public int OCOBulkID { get; set; }

    public override string ToString() { return ToString(SaveOptions.DisableFormatting); }
    public string ToString(SaveOptions saveOptions) {
      var x = new XElement(GetType().Name,
      GetType().GetProperties().Select(p => new XElement(p.Name, p.GetValue(this, null) + "")));
      return x.ToString(saveOptions);
    }
    public string ToString(string separator) {
      List<string> props = new List<string>();
      foreach (var prop in GetType().GetProperties().OrderBy(p=>p.Name))
        props.Add(prop.Name + ":" + prop.GetValue(this, new object[0]));
      return string.Join(separator, props);
    }
  }
}
