using HedgeHog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace IBApi {
  public static class Mixin {
  }
  public partial class Contract {
    private string HashKey => Instrument +
      (IsCombo ? ":" + ComboLegs.OrderBy(l => l.Ratio).Select(l => $"{l.ConId}-{l.Ratio}").Flatter(":") : "");
    public double IntrinsicValue(double undePrice) =>
      !IsOption
      ? 0
      : IsCall
      ? (undePrice - Strike).Max(0)
      : IsPut
      ? (Strike - undePrice).Max(0)
      : 0;
    public double ExtrinsicValue(double optionPrice, double undePrice) =>
      !IsOption
      ? 0
      : IsCall
      ? optionPrice - (undePrice - Strike).Max(0)
      : IsPut
      ? optionPrice - (Strike - undePrice).Max(0)
      : 0;
    DateTime? _lastTradeDateOrContractMonth;
    public DateTime Expiration =>
      (_lastTradeDateOrContractMonth ??
      (_lastTradeDateOrContractMonth = LastTradeDateOrContractMonth.FromTWSDateString(DateTime.Now.Date))
      ).Value;

    public string Key => Instrument;
    public string Instrument => ComboLegsToString().IfEmpty((LocalSymbol.IfEmpty(Symbol)?.Replace(".", "") + "").ToUpper());

    static readonly ConcurrentDictionary<string, Contract> _contracts = new ConcurrentDictionary<string, Contract>(StringComparer.OrdinalIgnoreCase);
    public static IDictionary<string, Contract> Contracts => _contracts;
    public Contract AddToCache() {
      if(!_contracts.TryAdd(Key, this) && _contracts[Key].HashKey != HashKey)
        _contracts.AddOrUpdate(Key, this, (k, c) => this);
      return this;
    }
    public IEnumerable<Contract> UnderContract => (from cd in FromDetailsCache()
                                                   from cdu in ContractDetails.FromCache(cd.UnderSymbol)
                                                   select cdu.Contract);
    public IEnumerable<ContractDetails> FromDetailsCache() => ContractDetails.FromCache(this);
    public IEnumerable<Contract> FromCache() => FromCache(Key);
    public IEnumerable<T> FromCache<T>(Func<Contract, T> map) => FromCache(Key, map);
    public static IEnumerable<Contract> Cache() => Contracts.Values;
    public static IEnumerable<Contract> FromCache(string instrument) => Contracts.TryGetValue(instrument);
    public static IEnumerable<T> FromCache<T>(string instrument, Func<Contract, T> map) => Contracts.TryGetValue(instrument).Select(map);

    public double PipCost() => MinTick() * ComboMultiplier;
    public double MinTick() {
      return ContractDetails.FromCache(this)
      .Select(cd => cd.MinTick)
      .Where(mt => mt > 0)
      .IfEmpty(() => Legs().SelectMany(c => ContractDetails.FromCache(c.c)).MaxByOrEmpty(cd => cd.MinTick).Select(cd => cd.MinTick).Take(1))
      .Count(1, _ => Debugger.Break(), _ => Debugger.Break())
      .DefaultIfEmpty(0.01)
      .Single();
    }
    public int ComboMultiplier => new[] { Multiplier }.Concat(Legs().Select(l => l.c.Multiplier)).Where(s => !s.IsNullOrWhiteSpace()).DefaultIfEmpty("1").Select(int.Parse).First();
    public bool IsCombo => ComboLegs?.Any() == true;
    public bool IsCall => IsOption && Right == "C";
    public bool IsPut => IsOption && Right == "P";
    public bool IsOption => SecType == "OPT" || SecType == "FOP";
    public bool IsFutureOption => SecType == "FOP";
    public bool HasFutureOption => IsFutureOption || Legs().Any(l => l.c.IsFutureOption);
    public bool IsFuture => SecType == "FUT" || secType == "CONTFUT";
    public bool IsButterFly => ComboLegs?.Any() == true && String.Join("", comboLegs.Select(l => l.Ratio)) == "121";
    public double ComboStrike() => Strike > 0 ? Strike : LegsEx().Sum(c => c.contract.strike * c.leg.Ratio) / LegsEx().Sum(c => c.leg.Ratio);
    public int ReqId { get; set; }

    string SecTypeToString() => SecType == "OPT" ? "" : " " + SecType;
    string ExpirationToString() => IsOption && LocalSymbol.IsNullOrWhiteSpace() || IsFutureOption ? " " + LastTradeDateOrContractMonth : "";
    public string ShortString => Right + Strike;
    public string ShortWithDate => LastTradeDateOrContractMonth?.Substring(4) + " " + ShortString;
    public override string ToString() =>
      ComboLegsToString().IfEmpty($"{LocalSymbol ?? Symbol}{SecTypeToString()}{ExpirationToString()}");// {Exchange} {Currency}";
    internal string ComboLegsToString() =>
      Legs()
      .ToArray()
      .With(legs => (legs, r: legs.Select(l => l.r).DefaultIfEmpty().Max())
      .With(t =>
      t.legs
      .Select(l => l.c.Instrument + (t.r > 1 ? ":" + l.r : ""))
      .OrderBy(s => s)
      .ToArray()
      .MashDiffs()));
    public IEnumerable<(Contract c, int r)> Legs() =>
      (from l in ComboLegs ?? new List<ComboLeg>()
       join c in Contracts.Select(cd => cd.Value) on l.ConId equals c.ConId
       select (c, l.Ratio)
       ).Memoize();
    public IEnumerable<(Contract contract, ComboLeg leg)> LegsEx() =>
      (from l in ComboLegs ?? new List<ComboLeg>()
       join c in Contracts.Select(cd => cd.Value) on l.ConId equals c.ConId
       select (c, l)
       ).Memoize();
  }
  public static class ContractMixins {
    public static bool IsIndex(this Contract c) => c.SecType == "IND";
  }

}
