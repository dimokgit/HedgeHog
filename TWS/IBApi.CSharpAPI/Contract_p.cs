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
    public DateTime LastTradeDateOrContractMonth2 =>
      (_lastTradeDateOrContractMonth ??
      (_lastTradeDateOrContractMonth = LastTradeDateOrContractMonth.FromTWSDateString())
      ).Value;

    public string Key => Instrument;
    public string Instrument => ComboLegsToString().IfEmpty((LocalSymbol.IfEmpty(Symbol)?.Replace(".", "") + "").ToUpper());

    static readonly ConcurrentDictionary<string, Contract> _contracts = new ConcurrentDictionary<string, Contract>(StringComparer.OrdinalIgnoreCase);
    public static IDictionary<string, Contract> Contracts => _contracts;
    public Contract AddToCache() { _contracts.TryAdd(Key, this); return this; }
    public IEnumerable<ContractDetails> FromDetailsCache() => ContractDetails.FromCache(this);
    public IEnumerable<Contract> FromCache() => FromCache(Key);
    public IEnumerable<T> FromCache<T>(Func<Contract, T> map) => FromCache(Key, map);
    public static IEnumerable<Contract> Cache() => Contracts.Values;
    public static IEnumerable<Contract> FromCache(string instrument) => Contracts.TryGetValue(instrument);
    public static IEnumerable<T> FromCache<T>(string instrument, Func<Contract, T> map) => Contracts.TryGetValue(instrument).Select(map);

    public double MinTick() {
      return ContractDetails.FromCache(this)
      .Select(cd => cd.MinTick)
      .Where(mt => mt > 0)
      .IfEmpty(() => Legs().SelectMany(c => ContractDetails.FromCache(c)).MaxByOrEmpty(cd => cd.MinTick).Select(cd => cd.MinTick).Take(1))
      .Count(1, _ => Debugger.Break(), _ => Debugger.Break())
      .DefaultIfEmpty(0.01)
      .Single();
    }
    public int ComboMultiplier => new[] { Multiplier }.Concat(Legs().Select(l => l.Multiplier)).Where(s => !s.IsNullOrWhiteSpace()).DefaultIfEmpty("1").Select(int.Parse).First();
    public bool IsCombo => ComboLegs?.Any() == true;
    public bool IsCall => IsOption && Right == "C";
    public bool IsPut => IsOption && Right == "P";
    public bool IsOption => SecType == "OPT";
    public bool IsButterFly => ComboLegs?.Any() == true && String.Join("", comboLegs.Select(l => l.Ratio)) == "121";
    public double ComboStrike() => Strike > 0 ? Strike : Legs().Select(c => c.strike).DefaultIfEmpty().Average();
    public int ReqId { get; set; }

    string SecTypeToString() => SecType == "OPT" ? "" : " " + SecType;
    public override string ToString() => ComboLegsToString().IfEmpty($"{LocalSymbol ?? Symbol}{SecTypeToString()}");// {Exchange} {Currency}";
    internal string ComboLegsToString() =>
      Legs().Select(c => c.Instrument)
      .ToArray()
      .MashDiffs();
    public IEnumerable<Contract> Legs() =>
      (from l in ComboLegs ?? new List<ComboLeg>()
       join c in Contracts.Select(cd => cd.Value) on l.ConId equals c.ConId
       select c
       );
    public IEnumerable<(Contract contract, ComboLeg leg)> LegsEx() =>
      (from l in ComboLegs ?? new List<ComboLeg>()
       join c in Contracts.Select(cd => cd.Value) on l.ConId equals c.ConId
       select (c, l)
       );
  }
  public static class ContractMixins {
    public static bool IsIndex(this Contract c) => c.SecType == "IND";
  }

}
