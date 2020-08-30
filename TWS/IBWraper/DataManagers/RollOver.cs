using IBApi;
using System.Collections.Generic;

namespace IBApp {
  public struct RollOver {
    public Contract under { get; }
    public Contract roll { get; }
    public int days { get; }
    public double bid { get; }
    public double ppw { get; }
    public double amount { get; }
    public double dpw { get; }
    public double perc { get; }
    public double delta { get; }

    public RollOver(Contract under, Contract roll, int days, double bid, double ppw, double amount, double dpw, double perc, double delta) {
      this.under = under;
      this.roll = roll;
      this.days = days;
      this.bid = bid;
      this.ppw = ppw;
      this.amount = amount;
      this.dpw = dpw;
      this.perc = perc;
      this.delta = delta;
    }

    public override bool Equals(object obj) => obj is RollOver other && EqualityComparer<Contract>.Default.Equals(under, other.under) && EqualityComparer<Contract>.Default.Equals(roll, other.roll) && days == other.days && bid == other.bid && ppw == other.ppw && amount == other.amount && dpw == other.dpw && perc == other.perc && delta == other.delta;

    public override int GetHashCode() {
      var hashCode = -716385779;
      hashCode = hashCode * -1521134295 + EqualityComparer<Contract>.Default.GetHashCode(under);
      hashCode = hashCode * -1521134295 + EqualityComparer<Contract>.Default.GetHashCode(roll);
      hashCode = hashCode * -1521134295 + days.GetHashCode();
      hashCode = hashCode * -1521134295 + bid.GetHashCode();
      hashCode = hashCode * -1521134295 + ppw.GetHashCode();
      hashCode = hashCode * -1521134295 + amount.GetHashCode();
      hashCode = hashCode * -1521134295 + dpw.GetHashCode();
      hashCode = hashCode * -1521134295 + perc.GetHashCode();
      hashCode = hashCode * -1521134295 + delta.GetHashCode();
      return hashCode;
    }

    public void Deconstruct(out Contract under, out Contract roll, out int days, out double bid, out double ppw, out double amount, out double dpw, out double perc, out double delta) {
      under = this.under;
      roll = this.roll;
      days = this.days;
      bid = this.bid;
      ppw = this.ppw;
      amount = this.amount;
      dpw = this.dpw;
      perc = this.perc;
      delta = this.delta;
    }

    public static implicit operator (Contract under, Contract roll, int days, double bid, double ppw, double bidAmount, double, double, double delta)(RollOver value) => (value.under, value.roll, value.days, value.bid, value.ppw, value.amount, value.dpw, value.perc, value.delta);
    public static implicit operator RollOver((Contract under, Contract roll, int days, double bid, double ppw, double bidAmount, double, double, double delta) value) => new RollOver(value.under, value.roll, value.days, value.bid, value.ppw, value.bidAmount, value.Item7, value.Item8, value.delta);
  }
}
