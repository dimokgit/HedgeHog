using HedgeHog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace IBApi {
  public partial class Contract {
    public bool IsCombo => ComboLegs?.Any() == true;
    public string Instrument => ComboLegsToString().IfEmpty((LocalSymbol?.Replace(".", "") + "").ToUpper());

    public static readonly ConcurrentDictionary<string, ContractDetails> ContractDetails = new ConcurrentDictionary<string, ContractDetails>(StringComparer.OrdinalIgnoreCase);
    public static readonly ConcurrentDictionary<string, Contract> Contracts = new ConcurrentDictionary<string, Contract>(StringComparer.OrdinalIgnoreCase);
    public Contract AddToCache() { Contracts.TryAdd(Instrument, this); return this; }

    public bool IsButterFly => ComboLegs?.Any() == true && String.Join("", comboLegs.Select(l => l.Ratio)) == "121";

    public override string ToString() => ComboLegsToString().IfEmpty($"{LocalSymbol ?? Symbol} {SecType}");// {Exchange} {Currency}";
    internal string ComboLegsToString() =>
      (from l in ComboLegs ?? new List<ComboLeg>()
       join c in Contracts.Select(cd => cd.Value) on l.ConId equals c.ConId
       select c.LocalSymbol
       )
      .ToArray()
      .Mash();
  }
  public static class ContractMixins {
    public static bool IsOption(this Contract c) => c.SecType == "OPT";
  }

}
