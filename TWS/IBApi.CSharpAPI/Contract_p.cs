using HedgeHog;
using HedgeHog.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace IBApi {
  public partial class Contract :IEquatable<Contract> {
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
      !HasOptions
      ? 0
      : LegsOrMe().Sum(o => o.ExtrinsicValueImpl(optionPrice, undePrice));
    private double ExtrinsicValueImpl(double optionPrice, double undePrice) => IsCall
          ? optionPrice - (undePrice - Strike).Max(0)
          : IsPut
          ? optionPrice - (Strike - undePrice).Max(0)
          : 0;

    DateTime? _lastTradeDateOrContractMonth;
    public DateTime Expiration => LegsOrMe(l => l.LastTradeDateOrContractMonth).Max().FromTWSDateString(DateTime.Now.Date);
    public string LastTradeDateOrContractMonth2 => LegsOrMe(l => l.LastTradeDateOrContractMonth).Max();

    public string Key => Instrument;
    public string Instrument => ComboLegsToString().IfEmpty((LocalSymbol.IfEmpty(Symbol, ConId + "")?.Replace(".", "") + "").ToUpper());

    static readonly ConcurrentDictionary<string, Contract> _contracts = new ConcurrentDictionary<string, Contract>(StringComparer.OrdinalIgnoreCase);
    public static IDictionary<string, Contract> Contracts => _contracts;
    public Contract AddToCache() {
      if(IsFuturesCombo) {
        Debug.WriteLine(this.ToJson(true));
      }
      if(!_contracts.TryAdd(Key, this) && _contracts[Key].HashKey != HashKey)
        _contracts.AddOrUpdate(Key, this, (k, c) => this);
      return this;
    }
    [Newtonsoft.Json.JsonIgnore]
    public IEnumerable<Contract> UnderContract => !IsOption ? new[] { this } : LegsOrMe(c => c.UnderContractImpl).Concat().Distinct().Count(1, i => { }, i => throw new Exception($"Too many UnderContracts in {this}"));
    public IEnumerable<Contract> UnderContractImpl => (from cd in FromDetailsCache()
                                                       where !cd.UnderSymbol.IsNullOrWhiteSpace()
                                                       from cdu in ContractDetails.FromCache(cd.UnderSymbol)
                                                       select cdu.Contract);
    public IEnumerable<ContractDetails> FromDetailsCache() => ContractDetails.FromCache(this);
    public IEnumerable<Contract> FromCache() => FromCache(Key);
    public IEnumerable<T> FromCache<T>(Func<Contract, T> map) => FromCache(Key, map);
    public static IEnumerable<Contract> Cache() => Contracts.Values;
    public static IEnumerable<Contract> FromCache(string instrument) => Contracts.TryGetValue(instrument);
    public static IEnumerable<T> FromCache<T>(string instrument, Func<Contract, T> map) => Contracts.TryGetValue(instrument).Select(map);
    public static IEnumerable<Contract> FromCache(Func<Contract, bool> filter) => Contracts.Where(kv => filter(kv.Value)).Select(kv => kv.Value);

    public double PipCost() => MinTick() * ComboMultiplier;

    public static IEnumerable<double> MinTickImpl(Contract c) {
      var x = c.FromDetailsCache();
      var y = c.UnderContract.SelectMany(uc => uc.FromDetailsCache());
      return x.Concat(y).Select(cd => cd.MinTick);
    }
    public double MinTick() => LegsOrMe(MinTickImpl).Concat().Max();
    public int ComboMultiplier => new[] { Multiplier }.Concat(Legs().Select(l => l.c.Multiplier)).Where(s => !s.IsNullOrWhiteSpace()).DefaultIfEmpty("1").Select(int.Parse).First();
    public bool IsFuturesCombo => LegsEx(l => l.c.IsFuture).Count() > 1;
    public bool IsCombo => LegsEx(l => l.c.IsOption).Count() > 1;
    public bool IsCall => IsOption && Right == "C";
    public bool IsPut => IsOption && Right == "P";
    public bool IsOption => SecType == "OPT" || SecType == "FOP";
    public bool IsFutureOption => SecType == "FOP";
    public bool HasFutureOption => IsFutureOption || Legs().Any(l => l.c.IsFutureOption);
    public bool IsFuture => SecType == "FUT" || secType == "CONTFUT";
    public bool IsIndex => SecType == "IND";
    public bool IsButterFly => ComboLegs?.Any() == true && String.Join("", comboLegs.Select(l => l.Ratio)) == "121";
    public bool IsCallPut => Legs().ToList().With(legs => legs.Any(l => l.c.IsCall) && legs.Any(l => l.c.IsPut));
    public double ComboStrike() => Strike > 0 ? Strike : LegsEx().With(cls => cls.Sum(c => c.contract.strike * c.leg.Ratio) / cls.Sum(c => c.leg.Ratio));
    public bool HasOptions => LegsOrMe(l => l.IsOption).Max();
    public int ReqMktDataId { get; set; }
    public IEnumerable<double> BreakEven(double openPrice) => Legs().Select(l => l.c.Strike + (l.c.IsCall ? 1 : -1) * openPrice);


    string SecTypeToString() => SecType == "OPT" ? "" : " " + SecType;
    string ExpirationToString() => IsOption && LocalSymbol.IsNullOrWhiteSpace() || IsFutureOption ? " " + LastTradeDateOrContractMonth : "";
    public string ShortString => ComboLegsToString((c, r, a) => c.Symbol + " " + LegLabel(r, a) + RightStrikeLabel(r, c), LocalSymbol.IfEmpty(Symbol));
    public string DateWithShort => ComboLegsToString((c, r, a)
      => c.LastTradeDateOrContractMonth.Substring(4) + " " + c.Symbol + " " + LegLabel(r, a) + RightStrikeLabel(r, c), LocalSymbol.IfEmpty(Symbol));
    public string ShortWithDate => ComboLegsToString((c, r, a) => c.Symbol + " " + c.LastTradeDateOrContractMonth.Substring(4) + " " + LegLabel(r, a) + RightStrikeLabel(r, c), LocalSymbol.IfEmpty(Symbol));
    public string ShortWithDate2 => ComboLegsToString((c, r, a)
      => c.Symbol + "" + _right.Match(c.LastTradeDateOrContractMonth.Substring(4)) + "" + LegLabel(r, a) + RightStrikeLabel2(r, c), LocalSymbol.IfEmpty(Symbol));
    public string FullString => $"{LocalSymbol.IfEmpty(Symbol)}:{Exchange}";
    public override string ToString() =>
      ComboLegsToString(LegToString, LocalSymbol.IfEmpty(Symbol))
      .IfEmpty(() => $"{LocalSymbol ?? Symbol}{SecTypeToString()}{ExpirationToString()}");// {Exchange} {Currency}";


    internal string ComboLegsToString() => ComboLegsToString((c, r, a) => LegLabel(r, a) + c.LocalSymbol, LocalSymbol.IfEmpty(Symbol));
    internal string ComboLegsToString(Func<Contract, int, string, string> label, string defaultFotNotOption) =>
      Legs()
      .ToArray()
      .With(legs => (legs, r: legs.Select(l => l.r).DefaultIfEmpty().Max())
      .With(t =>
      t.legs
      .Select(l => label(l.c, l.r, l.a))
      .OrderBy(s => s)
      .RunIfEmpty(() => IsOption ? label(this, 0, "") + "" : defaultFotNotOption + "")
      .ToArray()
      .MashDiffs()));

    static string LegToString(Contract c, int r, string a) => LegLabel(r, a) + (c.IsOption ? c.UnderContract.Select(u => c.ShortWithDate).SingleOrDefault() : c.Instrument);
    public static string LegLabel(int ratio, string action) => ratio == 0 ? "" : (action == "BUY" ? "+" : "-") + (ratio > 1 ? ratio + "" : "");
    static string RightStrikeLabel(int ratio, Contract c) => c.Right.IsNullOrEmpty() ? "" : (ratio.Abs() > 1 ? ":" : "") + c.Right + c.Strike;
    static Regex _right = new Regex(".{2}$");
    static string RightStrikeLabel2(int ratio, Contract c) => c.Right.IsNullOrEmpty() ? "" : (ratio.Abs() > 1 ? ":" : "") + c.Right + _right.Match(c.Strike + "");
    public IEnumerable<T> LegsOrMe<T>(Func<Contract, T> map) => LegsOrMe().Select(map);
    public IEnumerable<Contract> LegsOrMe() => Legs().Select(cl => cl.c).DefaultIfEmpty(this);
    public IEnumerable<T> Legs<T>(Func<(Contract c, int r, string a), T> map) => Legs().Select(map);
    public IEnumerable<(Contract c, int r, string a)> Legs() {
      if(ComboLegs == null) yield break;
      var x = (from l in ComboLegs
               join c in Contracts on l.ConId equals c.Value.ConId
               select (c.Value, l.Ratio, l.Action)
       );
      foreach(var t in x)
        yield return t;
    }
    public IEnumerable<T> LegsEx<T>(Func<(Contract c, ComboLeg leg), T> map) => LegsEx().Select(map);
    public IEnumerable<(Contract contract, ComboLeg leg)> LegsEx(Func<(Contract c, ComboLeg leg), bool> filter) => LegsEx().Where(filter);
    public IEnumerable<(Contract contract, ComboLeg leg)> LegsEx() {
      if(ComboLegs == null) yield break;
      var x = from l in ComboLegs
              join c in Contracts on l.ConId equals c.Value.ConId
              select (c.Value, l);
      foreach(var t in x)
        yield return t;
    }
    string _key() => ComboLegsToString((c, r, a) => LegLabel(r, a) + c.ConId, ConId + "");
    public IEnumerable<(string Context, bool Ok)> Ok => LegsOrMe(c => ($"Contract: {c}.Exchange={c.Exchange}", !c.Exchange.IsNullOrEmpty()));
    public void Check() => Ok.Where(t => !t.Ok).ForEach(ok => throw new Exception(ok.Context));

    public static bool operator !=(Contract a, Contract b) => !(a == b);
    public static bool operator ==(Contract a, Contract b) => a is null && b is null
      ? true
      : a is null || b is null
      ? false
      : a.Equals(b);
    public bool Equals(Contract other) => other is null && this is null
      ? true
      : other is null || this is null
      ? false
      : _key().Equals(other._key());
  }
  public static class ContractMixins {
    public static string ToLable(this ComboLeg l) => l.Ratio == 0 ? "" : (l.Action == "BUY" ? "+" : "-") + (l.Ratio > 1 ? l.Ratio + "" : "");
  }
}
