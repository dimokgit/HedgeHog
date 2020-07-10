using HedgeHog;
using System;

namespace IBApp {
  public struct MarketPrice {
    public double bid;
    public double ask;
    public DateTime time;
    public double delta;
    public double theta;
    public double avg;

    public MarketPrice(double bid, double ask, DateTime time, double delta, double theta) {
      this.bid = bid;
      this.ask = ask;
      this.time = time;
      this.delta = delta;
      this.theta = theta;
      this.avg = bid.Avg(ask);
    }

    public override bool Equals(object obj)
      => obj is MarketPrice other && bid == other.bid && ask == other.ask
      && time == other.time && delta == other.delta && theta.IfNaN(0) == other.theta.IfNaN(0);

    public override int GetHashCode() {
      var hashCode = 1697963223;
      hashCode = hashCode * -1521134295 + bid.GetHashCode();
      hashCode = hashCode * -1521134295 + ask.GetHashCode();
      hashCode = hashCode * -1521134295 + time.GetHashCode();
      hashCode = hashCode * -1521134295 + delta.GetHashCode();
      hashCode = hashCode * -1521134295 + theta.GetHashCode();
      return hashCode;
    }

    public void Deconstruct(out double bid, out double ask, out DateTime time, out double delta, out double theta) {
      bid = this.bid;
      ask = this.ask;
      time = this.time;
      delta = this.delta;
      theta = this.theta;
    }

    public static implicit operator (double bid, double ask, DateTime time, double delta, double theta)(MarketPrice value)
      => (value.bid, value.ask, value.time, value.delta, value.theta);
    public static implicit operator MarketPrice((double bid, double ask, DateTime time, double delta, double theta) value)
      => new MarketPrice(value.bid, value.ask, value.time, value.delta, value.theta);

    public static bool operator ==(MarketPrice left, MarketPrice right) {
      return left.Equals(right);
    }

    public static bool operator !=(MarketPrice left, MarketPrice right) {
      return !(left == right);
    }
  }
}