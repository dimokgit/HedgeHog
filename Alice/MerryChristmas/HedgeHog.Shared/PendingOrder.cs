using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  public class PendingOrder : IEquatable<PendingOrder> {
    Guid _uniqueId = Guid.NewGuid();
    public Guid UniqueId {
      get { return _uniqueId; }
      set { _uniqueId = value; }
    }
    public PendingOrder Parent { get; set; }
    public PendingOrder[] GetKids(IEnumerable<PendingOrder> others) {
      return others.Where(po => po.Parent == this).ToArray();
    }
    public bool HasKids(IEnumerable<PendingOrder> others) { return GetKids(others).Length > 0; }
    public string Pair { get; set; }
    public bool Buy { get; set; }
    public int Lot { get; set; }
    public double Rate { get; set; }
    public double Limit { get; set; }
    public double Stop { get; set; }
    public string TradeId { get; set; }
    public string OrderId { get; set; }
    public string RequestId { get; set; }
    public string Remark { get; set; }
    public PendingOrder(string tradeId,double stop,double limit ,string remark) {
      this.TradeId = tradeId;
      this.Stop = stop;
      this.Limit = limit;
      this.Remark = remark;
    }
    public PendingOrder(Trade trade){
      this.Pair = trade.Pair;
      this.Buy = trade.Buy;
      this.Lot = trade.Lots;
      this.Stop = trade.Stop;
      this.Limit = trade.Limit;
    }
    public PendingOrder(string pair, bool buy, int lot, double rate, double stop, double limit,string remark) :
      this(pair, buy, lot, rate, stop, limit,remark, "", "") { }
    public PendingOrder(string pair, bool buy, int lot,double rate, double stop, double limit,string remark, string orderId, string requestId) {
      this.Pair = pair;
      this.Buy = buy;
      this.Rate = rate;
      this.Lot = lot;
      this.Limit = limit;
      this.Stop = stop;
      this.Remark = remark;
      this.OrderId = orderId;
      this.RequestId = requestId;
    }

    public override string ToString() {
      return ToString(",");
    }
    public string ToString(string separator) {
      List<string> props = new List<string>();
      foreach(var prop in GetType().GetProperties())
        props.Add(prop.Name+":"+prop.GetValue(this,new object[0]));
      return string.Join(separator, props);
    }
    #region IEquatable<PendingOrer> Members

    public bool Equals(PendingOrder other) {
      return
        (this.Pair ?? this.TradeId) == (other.Pair  ?? other.TradeId) &&
      this.Buy == other.Buy &&
      this.Lot == other.Lot &&
      this.Rate == other.Rate &&
      this.Limit == other.Limit &&
      this.Stop == other.Stop;
    }
    public override bool Equals(Object obj) {
      if (obj == null) return base.Equals(obj);
      if (!(obj is PendingOrder))
        throw new InvalidCastException("The 'obj' argument is not an " + GetType().Name + " object.");
      else
        return Equals(obj as PendingOrder);
    }

    public override int GetHashCode() {
      return Pair.GetHashCode() ^ Buy.GetHashCode() ^ Lot ^ Rate.GetHashCode() ^ Limit.GetHashCode() ^ Stop.GetHashCode();
    }
    public static bool operator ==(PendingOrder or1, PendingOrder or2) {
      return object.Equals(or1, or2);
    }

    public static bool operator !=(PendingOrder or1, PendingOrder or2) {
      return !object.Equals(or1, or2);
    }

    #endregion
  }

}
