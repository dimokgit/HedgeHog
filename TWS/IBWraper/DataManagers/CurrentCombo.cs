using HedgeHog;
using IBApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;

namespace IBApp {
  public struct CurrentCombo {
    public string instrument;
    public double strikeAvg;
    public double underPrice;
    public (double up, double dn) breakEven;
    public (Contract contract, Contract[] options) combo;
    public double deltaBid;
    public double deltaAsk;
    public MarketPrice marketPrice { get; }
    public Contract option => combo.contract;
    public double priceRatio =>
      combo.options.Pairwise((o1, o2) => o1.Price.Ratio(o2.Price)).DefaultIfEmpty(double.NaN).First();
    public CurrentCombo(string instrument, MarketPrice marketPrice, double strikeAvg, double underPrice, (double up, double dn) breakEven, Contract contract, double deltaBid, double deltaAsk) : this(
      instrument, marketPrice, strikeAvg, underPrice, breakEven, (contract, new Contract[0]), deltaBid, deltaAsk
      ) {
    }
    public CurrentCombo(string instrument, MarketPrice marketPrice, double strikeAvg, double underPrice, (double up, double dn) breakEven, (Contract contract, Contract[] options) combo, double deltaBid, double deltaAsk) {
      this.instrument = instrument;
      this.marketPrice = marketPrice;
      this.strikeAvg = strikeAvg;
      this.underPrice = underPrice;
      this.breakEven = breakEven;
      this.combo = combo;
      this.deltaBid = deltaBid;
      this.deltaAsk = deltaAsk;
    }

    public override bool Equals(object obj) => obj is CurrentCombo other && instrument == other.instrument && marketPrice == other.marketPrice && strikeAvg == other.strikeAvg && underPrice == other.underPrice && breakEven.Equals(other.breakEven) && combo.Equals(other.combo) && deltaBid == other.deltaBid && deltaAsk == other.deltaAsk;

    public override int GetHashCode() {
      var hashCode = -653033887;
      hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(instrument);
      hashCode = hashCode * -1521134295 + strikeAvg.GetHashCode();
      hashCode = hashCode * -1521134295 + underPrice.GetHashCode();
      hashCode = hashCode * -1521134295 + breakEven.GetHashCode();
      hashCode = hashCode * -1521134295 + combo.GetHashCode();
      hashCode = hashCode * -1521134295 + deltaBid.GetHashCode();
      hashCode = hashCode * -1521134295 + deltaAsk.GetHashCode();
      hashCode = hashCode * -1521134295 + marketPrice.GetHashCode();
      return hashCode;
    }

    public void Deconstruct(out string instrument, MarketPrice marketPrice, out double strikeAvg, out double underPrice, out (double up, double dn) breakEven, out (Contract contract, Contract[] options) combo, out double deltaBid, out double deltaAsk) {
      instrument = this.instrument;
      marketPrice = this.marketPrice;
      strikeAvg = this.strikeAvg;
      underPrice = this.underPrice;
      breakEven = this.breakEven;
      combo = this.combo;
      deltaBid = this.deltaBid;
      deltaAsk = this.deltaAsk;
    }

    public object ToAnon() => new {
      instrument,
      marketPrice,
      strikeAvg,
      underPrice,
      breakEven,
      combo,
      deltaBid,
      deltaAsk
    };
    public override string ToString() => ToAnon().ToString();

    public static implicit operator (string instrument, MarketPrice marketPrice, double strikeAvg, double underPrice, (double up, double dn) breakEven, Contract contract, double deltaBid, double deltaAsk)(CurrentCombo value) => (value.instrument, value.marketPrice, value.strikeAvg, value.underPrice, value.breakEven, value.combo.contract, value.deltaBid, value.deltaAsk);
    public static implicit operator (string instrument, MarketPrice marketPrice, double strikeAvg, double underPrice, (double up, double dn) breakEven, (Contract contract, Contract[] options) combo, double deltaBid, double deltaAsk)(CurrentCombo value) => (value.instrument, value.marketPrice, value.strikeAvg, value.underPrice, value.breakEven, value.combo, value.deltaBid, value.deltaAsk);

    public static implicit operator CurrentCombo((string instrument, MarketPrice marketPrice, double strikeAvg, double underPrice, (double up, double dn) breakEven, Contract contract, double deltaBid, double deltaAsk) value) => new CurrentCombo(value.instrument, value.marketPrice, value.strikeAvg, value.underPrice, value.breakEven, value.contract, value.deltaBid, value.deltaAsk);
    public static implicit operator CurrentCombo((string instrument, MarketPrice marketPrice, double strikeAvg, double underPrice, (double up, double dn) breakEven, (Contract contract, Contract[] options) combo, double deltaBid, double deltaAsk) value) => new CurrentCombo(value.instrument, value.marketPrice, value.strikeAvg, value.underPrice, value.breakEven, value.combo, value.deltaBid, value.deltaAsk);

    public static bool operator ==(CurrentCombo left, CurrentCombo right) {
      return left.Equals(right);
    }

    public static bool operator !=(CurrentCombo left, CurrentCombo right) {
      return !(left == right);
    }
  }
}
