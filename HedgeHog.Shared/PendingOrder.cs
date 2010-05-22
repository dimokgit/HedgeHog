using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  public class PendingOrer : IEquatable<PendingOrer> {
    public string Pair { get; set; }
    public bool Buy { get; set; }
    public int Lot { get; set; }
    public double TakeProfit { get; set; }
    public double StopLoss { get; set; }
    public string OrderId { get; set; }
    public string RequestId { get; set; }
    public PendingOrer(string pair, bool buy, int lot, double takeProfit, double stopLoss) :
      this(pair, buy, lot, takeProfit, stopLoss, "", "") { }
    public PendingOrer(string pair, bool buy, int lot, double takeProfit, double stopLoss, string orderId, string requestId) {
      this.Pair = pair;
      this.Buy = buy;
      this.Lot = lot;
      this.TakeProfit = takeProfit;
      this.StopLoss = stopLoss;
      this.OrderId = orderId;
      this.RequestId = requestId;
    }

    #region IEquatable<PendingOrer> Members

    public bool Equals(PendingOrer other) {
      return this.Pair == other.Pair &&
      this.Buy == other.Buy &&
      this.Lot == other.Lot &&
      this.TakeProfit == other.TakeProfit &&
      this.StopLoss == other.StopLoss;
    }
    public override bool Equals(Object obj) {
      if (obj == null) return base.Equals(obj);
      if (!(obj is PendingOrer))
        throw new InvalidCastException("The 'obj' argument is not an " + GetType().Name + " object.");
      else
        return Equals(obj as PendingOrer);
    }

    public override int GetHashCode() {
      return Pair.GetHashCode() ^ Buy.GetHashCode() ^ Lot ^ TakeProfit.GetHashCode() ^ StopLoss.GetHashCode();
    }
    public static bool operator ==(PendingOrer or1, PendingOrer or2) {
      return object.Equals(or1, or2);
    }

    public static bool operator !=(PendingOrer or1, PendingOrer or2) {
      return !object.Equals(or1, or2);
    }

    #endregion
  }

}
