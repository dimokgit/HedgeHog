using HedgeHog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace IBApi {
  public static class Mixin {
  }
  public partial class Contract {
    DateTime? _lastTradeDateOrContractMonth;
    public DateTime LastTradeDateOrContractMonth2 =>
      (_lastTradeDateOrContractMonth ?? 
      (_lastTradeDateOrContractMonth = LastTradeDateOrContractMonth.FromTWSDateString())
      ).Value; 

    public string Key => Instrument;
    public bool IsCombo => ComboLegs?.Any() == true;
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

    public bool IsButterFly => ComboLegs?.Any() == true && String.Join("", comboLegs.Select(l => l.Ratio)) == "121";

    string SecTypeToString() => SecType == "OPT" ? "" : " " + SecType;
    public override string ToString() => ComboLegsToString().IfEmpty($"{LocalSymbol ?? Symbol}{SecTypeToString()}");// {Exchange} {Currency}";
    internal string ComboLegsToString() =>
      (from l in ComboLegs ?? new List<ComboLeg>()
       join c in Contracts.Select(cd => cd.Value) on l.ConId equals c.ConId
       select c.LocalSymbol
       )
      .ToArray()
      .MashDiffs();
  }
  public static class ContractMixins {
    public static bool IsOption(this Contract c) => c.SecType == "OPT";
    public static bool IsIndex(this Contract c) => c.SecType == "IND";
  }

}
