using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using System.Xml.Linq;
using System.ComponentModel;

namespace HedgeHog.Shared {
  public class OrderEventArgs : EventArgs {
    public Order Order { get; set; }
    public OrderEventArgs(Order newOrder) {
      this.Order = newOrder;
    }
  }

  public static class OrderExtentions {
    public static Order[] IsBuy(this ICollection<Order> orders, bool isBuy) {
      return orders.Where(o => o.IsBuy == isBuy).ToArray();
    }
    public static Order OrderById(this ICollection<Order> orders, string orderId) {
      return orders.Where(o => o.OrderID == orderId).SingleOrDefault();
    }
    public static Order OrderByRequestId(this ICollection<Order> orders, string requestId) {
      return orders.Where(o => o.RequestID == requestId).SingleOrDefault();
    }
  }

  [DataContract]
  public class Order : PositionBase {
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
    [UpdateOnUpdate]
    public Boolean NetQuantity { get; set; }
    public bool IsNetOrder { get { return NetQuantity; } }
    //public bool IsEntryOrder { get { return !NetQuantity; } }
    [DataMember]
    public String BS { get; set; }
    [DataMember]
    [UpdateOnUpdate]
    public String Stage { get; set; }
    [DataMember]
    [UpdateOnUpdate]
    public int Side { get; set; }
    [DataMember]
    public String Type { get; set; }
    [DataMember]
    [UpdateOnUpdate]
    public String FixStatus { get; set; }
    [DataMember]
    [UpdateOnUpdate]
    public String Status { get; set; }
    [DataMember]
    [UpdateOnUpdate]
    public int StatusCode { get; set; }
    [DataMember]
    [UpdateOnUpdate]
    public String StatusCaption { get; set; }
    [DataMember]
    [UpdateOnUpdate]
    public int Lot { get; set; }
    [DataMember]
    [UpdateOnUpdate]
    public Double AmountK { get; set; }
    [DataMember]
    [UpdateOnUpdate]
    public Double Rate { get; set; }
    [DataMember]
    [UpdateOnUpdate]
    public Double SellRate { get; set; }
    [DataMember]
    [UpdateOnUpdate]
    public Double BuyRate { get; set; }
    [DataMember]
    [UpdateOnUpdate]
    public Double Stop { get; set; }
    [DataMember]
    public Double UntTrlMove { get; set; }
    [DataMember]
    [UpdateOnUpdate]
    public Double Limit { get; set; }
    [DataMember]
    DateTime _Time;
    public DateTime Time {
      get { return _Time; }
      set { 
        _Time = value;
        TimeLocal = TimeZoneInfo.ConvertTimeFromUtc(Time, TimeZoneInfo.Local);
      }
    }
    [DataMember]
    public DateTime TimeLocal { get; set; }

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
    [UpdateOnUpdate]
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

    [DataMember]
    [UpdateOnUpdate]
    public double StopInPips { get; set; }
    [DataMember]
    [UpdateOnUpdate]
    public double LimitInPips { get; set; }

    [DataMember]
    [UpdateOnUpdate]
    public double StopInPoints { get; set; }
    [DataMember]
    [UpdateOnUpdate]
    public double LimitInPoints { get; set; }

    [DataMember]
    [UpdateOnUpdate]
    public double PipsTillRate { get; set; }

    public double GetStopInPips(Func<string, double, double> inPips) { return TypeStop > 1 ? -Math.Abs(Stop) : inPips(Pair, Stop - Rate); }
    public double GetStopInPoints(Func<string, double, double> inPoints) {
      if (TypeStop == 1) return Stop;
      var stopInPoints = inPoints(Pair, Stop);
      return Rate + stopInPoints;
    }
    public double GetLimitInPips(Func<string, double, double> inPips) { return TypeLimit > 1 ? Math.Abs(Limit) : Math.Abs(inPips(Pair, Stop - Rate)); }
    public double GetLimitInPoints(Func<string, double, double> inPoints) {
      if (TypeLimit == 1) return Limit;
      var limitInPoints = inPoints(Pair, Limit);
      return Rate - limitInPoints;
    }

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
