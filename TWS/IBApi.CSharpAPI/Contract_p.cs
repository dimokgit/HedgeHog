using HedgeHog;
using HedgeHog.Core;
using HedgeHog.DateTimeZone;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace IBApi {
  public partial class Contract :IEquatable<Contract> {

    public Contract SetTestConId(bool isInTest, int multiplier) {
      if(isInTest && ConId == 0) {
        if(Symbol.IsNullOrEmpty()) Symbol = LocalSymbol;
        if(LocalSymbol.IsNullOrEmpty()) LocalSymbol = Symbol;
        if(LocalSymbol.IsNullOrEmpty() && Symbol.IsNullOrEmpty()) throw new Exception("LocalSymbol and Symbol both empty.");
        ConId = LegsOrMe().Select(c => c.LocalSymbol).Flatter(",").GetHashCode();
        if(multiplier > 0) Multiplier = multiplier + "";
        AddToCache();
      }
      return this;
    }
    private string HashKey => Instrument +
      (IsOptionsCombo ? ":" + ComboLegs.OrderBy(l => l.Ratio).Select(l => $"{l.ConId}-{l.Ratio}").Flatter(":") : "");
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

    private const string BAG = "BAG";
    public DateTime Expiration => LegsOrMe(l => l.LastTradeDateOrContractMonth).Max().FromTWSDateString(DateTime.Now.Date);
    public bool IsExpired => Expiration < DateTime.Now.Date;
    public string LastTradeDateOrContractMonth2 => LegsOrMe(l => l.LastTradeDateOrContractMonth).Max();

    public string Key => Instrument;
    string SafeInstrument => LocalSymbol.IfEmpty(Symbol, ConId + "");
    public string Instrument => ComboLegsToString().IfEmpty((SafeInstrument?.Replace(".", "") + "").ToUpper());

    static readonly ConcurrentDictionary<string, Contract> _contracts = new ConcurrentDictionary<string, Contract>(StringComparer.OrdinalIgnoreCase);
    public static IDictionary<string, Contract> Contracts => _contracts;
    public static IEnumerable<(int[] conIds, Contract contract)> ContractConIds => _contracts.Select(c => (c.Value.ConIds.ToArray(), c.Value));
    //public Contract FromCache() => _contracts.TryGetValue(Key, out var c).With(_ => c);
    public Contract AddToCache() {
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
    public IEnumerable<ContractDetails> FromDetailsCache() => FromCache().SelectMany(c => ContractDetails.FromCache(c));
    static object _gate = new object();
    public IEnumerable<Contract> FromCache() {
      lock(_gate)
        return FromCache(Key).Concat(FromCache(ConId)).Take(1).IfEmpty(()
          => IsBag
          ? new[] { new ContractDetails() { Contract = this }.AddToCache().Contract }
          : new Contract[0]
          );
    }

    public bool IsBag => SecType == BAG;
    public IEnumerable<T> FromCache<T>(Func<Contract, T> map) => FromCache(Key, map);
    public static IEnumerable<Contract> Cache() => Contracts.Values;
    public static IEnumerable<Contract> FromCache(string instrument) => Contracts.TryGetValue(instrument);
    public static IEnumerable<Contract> FromCache(int conId) => 
      Contracts.Where(c => conId !=0 && c.Value.ConId == conId).Take(1).Select(kv => kv.Value);
    public static IEnumerable<T> FromCache<T>(string instrument, Func<Contract, T> map) => Contracts.TryGetValue(instrument).Select(map);
    public static IEnumerable<Contract> FromCache(Func<Contract, bool> filter) => Contracts.Where(kv => filter(kv.Value)).Select(kv => kv.Value);

    public double PipCost() => MinTick() * ComboMultiplier;

    public static IEnumerable<double> MinTickImpl(Contract c) {
      var x = c.FromDetailsCache();
      var y = c.UnderContract.SelectMany(uc => uc.FromDetailsCache());
      return x.Concat(y).Select(cd => cd.MinTick);
    }
    public double MinTick() => LegsOrMe(MinTickImpl).Concat().Max();
    public int ComboMultiplier => new[] { Multiplier }.Concat(Legs().Select(l => l.c.Multiplier)).Where(s => !s.IsNullOrWhiteSpace()).DefaultIfEmpty("1").Select(int.Parse).Last();
    public bool IsCombo => IsBag;
    public bool IsFuturesCombo => LegsEx(l => l.c.IsFuture).Count() > 1;
    public bool IsStocksCombo => LegsEx(l => l.c.IsStock).Count() > 1;
    public bool IsHedgeCombo => IsStocksCombo || IsFuturesCombo;
    public bool IsOptionsCombo => LegsEx(l => l.c.IsOption).Count() > 1;
    public bool IsCall => IsOption && Right == "C";
    public bool IsPut => IsOption && Right == "P";
    public bool IsOption => SecType == "OPT" || SecType == "FOP";
    public bool IsFutureOption => SecType == "FOP";
    public bool HasFutureOption => IsFutureOption || Legs().Any(l => l.c.IsFutureOption);
    public bool IsFuture => SecType == "FUT" || secType == "CONTFUT";
    public bool IsIndex => SecType == "IND";
    public bool IsStock => SecType == "STK";
    public bool IsButterFly => ComboLegs?.Any() == true && String.Join("", comboLegs.Select(l => l.Ratio)) == "121";
    public bool IsCallPut => Legs().ToList().With(legs => legs.Any(l => l.c.IsCall) && legs.Any(l => l.c.IsPut));
    public double ComboStrike() => Strike > 0 ? Strike : LegsEx().With(cls => cls.Sum(c => c.contract.strike * c.leg.Ratio) / cls.Sum(c => c.leg.Ratio));
    public bool HasOptions => LegsOrMe(l => l.IsOption).Max();
    public int ReqMktDataId { get; set; }
    public int DeltaSign => IsPut ? -1 : 1;
    public IEnumerable<double> BreakEven(double openPrice) => Legs().Select(l => l.c.Strike + (l.c.IsCall ? 1 : -1) * openPrice);

    public static IList<DateTimeOffset> _LiquidHoursDefault = new List<DateTimeOffset> {

    };
    public IList<DateTimeOffset> _LiquidHours = null;
    [JsonIgnore]
    public IList<DateTimeOffset> LiquidHours => _LiquidHours ?? (_LiquidHours
      = FromDetailsCache().Select(LiquidHoursImpl).SingleOrDefault(lh => lh.Any()) ?? GetTodayLiquidRange());
    static IList<DateTimeOffset> LiquidHoursImpl(ContractDetails cd) =>
      (from ranges in cd.LiquidHours.Split(';')
       let range = ranges.Split('-')
       where range.Length == 2
       from t in range
       select DateTimeOffset.ParseExact(t, "yyyyMMdd:HHmm", null, DateTimeStyles.None).InTZ(TimeZoneFromContractDetails(cd)).InNewYork()
       )
      .Take(2)
      .ToArray();
    IList<DateTimeOffset> GetTodayLiquidRange() =>
      (from t in new[] { "0930", "1600" }
       select DateTimeOffset.ParseExact(t, "yyyyMMdd:HHmm", null, DateTimeStyles.None).InTZ(TimeZone).InNewYork()
       ).ToArray();
    public bool IsPreRTH(DateTime serverTime) => !serverTime.Between(LiquidHours);

    #region TimeZone
    TimeZoneInfo _timeZone = null;
    TimeZoneInfo TimeZone => (_timeZone ?? (_timeZone = FromDetailsCache().Select(TimeZoneFromContractDetails).Single()));
    static TimeZoneInfo TimeZoneFromContractDetails(ContractDetails cd) => TimeZoneInfo.FindSystemTimeZoneById(ParseTimeZone(cd));
    static string ParseTimeZone(ContractDetails cd) => Regex.Match(cd.TimeZoneId, @".+\((.+)\)").Groups[1] + "";
    #endregion

    string SecTypeToString() => SecType == "OPT" ? "" : " " + SecType;
    string ExpirationToString() => IsOption && LocalSymbol.IsNullOrWhiteSpace() || IsFutureOption ? " " + LastTradeDateOrContractMonth : "";
    public string ShortSmart => (IsOptionsCombo && Legs((c, l) => c.LastTradeDateOrContractMonth).Distinct().Count() == 1) ? DateWithShort : ShortWithDate;
    public string ShortString => ComboLegsToString((c, r, a) => c.Symbol + " " + LegLabel(r, a) + RightStrikeLabel(r, c), () => LocalSymbol.IfEmpty(Symbol));
    public string DateWithShort => ComboLegsToString((c, r, a)
      => ShowLastDate(c, s => s.Substring(4) + " ") + c.Symbol + " " + LegLabel(r, a) + RightStrikeLabel(r, c), () => LocalSymbol.IfEmpty(Symbol));
    public string ShortWithDate => ComboLegsToString((c, r, a)
      => c.Symbol + " " + ShowLastDate(c, s => s.Substring(4) + " ") + LegLabel(r, a) + RightStrikeLabel(r, c), () => LocalSymbol.IfEmpty(Symbol));
    public string ShortWithDate2 => ComboLegsToString((c, r, a) => c.Symbol + "" + ShowLastDate(c, s => _right.Match(s.Substring(4)) + "") + LegLabel(r, a) + RightStrikeLabel2(r, c), () => LocalSymbol.IfEmpty(Symbol));
    public string FullString => $"{LocalSymbol.IfEmpty(Symbol)}:{Exchange}";
    public override string ToString() =>
      ComboLegsToString(LegToString, () => LocalSymbol.IfEmpty(Symbol))
      .IfEmpty(() => $"{LocalSymbol.IfEmpty(Symbol)}{SecTypeToString()}{ExpirationToString()}");// {Exchange} {Currency}";

    static string ShowLastDate(Contract c, Func<string, string> show) => c.LastTradeDateOrContractMonth.IfTrue(s => !s.IsNullOrEmpty(), s => show(s), "");

    internal string ComboLegsToString() => ComboLegsToString((c, r, a) => LegLabel(r, a) + c.LocalSymbol, () => LocalSymbol.IfEmpty(Symbol));
    internal string ComboLegsToString(Func<Contract, int, string, string> label, Func<string> defaultFotNotOption/*,Func<(Contract c, int r, string a),string> orderBy = null*/) {
      var legs = Legs();
      //if(orderBy!=null)
      return legs
      //.OrderBy(l => l.c.Instrument)
      .Select(l => label(l.c, l.r, l.a))
      .RunIfEmpty(() => IsOption ? label(this, 0, "") + "" : defaultFotNotOption() + "")
      .ToArray()
      .MashDiffs();
    }
    static string LegToString(Contract c, int r, string a) => LegLabel(r, a) + (c.IsOption ? c.UnderContract.Select(u => c.ShortWithDate).SingleOrDefault() : c.Instrument);
    public static string LegLabel(int ratio, string action) => ratio == 0 ? "" : (action == "BUY" ? "+" : "-") + (ratio > 1 ? ratio + "" : "");
    static string RightStrikeLabel(int ratio, Contract c) => c.Right.IsNullOrEmpty() ? "" : (ratio.Abs() > 1 ? ":" : "") + c.Right + c.Strike;
    static Regex _right = new Regex(".{2}$");
    static string RightStrikeLabel2(int ratio, Contract c) => c.Right.IsNullOrEmpty() ? "" : (ratio.Abs() > 1 ? ":" : "") + c.Right + _right.Match(c.Strike + "");

    public IEnumerable<T> LegsForHedge<T>(string key, Func<(Contract c, ComboLeg leg), T> map)
      => LegsEx().OrderByDescending(l => l.contract.Key == key).Select(map);
    public IEnumerable<(Contract c, ComboLeg leg)> LegsForHedge(string key) => LegsEx().OrderByDescending(l => l.contract.Key == key);
    public IEnumerable<T> LegsOrMe<T>(Func<Contract, T> map) => LegsOrMe().Select(map);
    public IEnumerable<Contract> LegsOrMe() => Legs().Select(cl => cl.c).DefaultIfEmpty(this);

    public IEnumerable<(Contract c, int r, string a)> Legs() => Legs((c, l) => (c, l.Ratio, l.Action));

    public IEnumerable<T> LegsEx<T>(Func<(Contract c, ComboLeg leg), T> map) => LegsEx().Select(map);
    public IEnumerable<(Contract contract, ComboLeg leg)> LegsEx(Func<(Contract c, ComboLeg leg), bool> filter) => LegsEx().Where(filter);
    public IEnumerable<(Contract contract, ComboLeg leg)> LegsEx() => Legs((c, l) => (c, l));

    public IEnumerable<T> Legs<T>(Func<Contract, ComboLeg, T> map) {
      if(ComboLegs == null) yield break;
      var x = (from l in ComboLegs
               join c in Contracts on l.ConId equals c.Value.ConId
               //orderby l.Action ascending, l.Ratio descending, c.Value.ConId
               select map(c.Value, l)
       );
      foreach(var t in x)
        yield return t;
    }
    public string _key() {
      return ComboLegsToString((c, r, a) => $"{CCId(c.ConId)}{LegLabel(r, a)}", () => CCId(ConId) + "");
      int CCId(int cid) {
        if(cid == 0)
          throw new Exception(new { Instrument, ConId = "Is Zero" } + "");
        return cid;
      }
    }
    public IEnumerable<int> ConIds => LegsOrMe(c => c.ConId).Where(cid => cid != 0);
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
