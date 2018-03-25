using HedgeHog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace IBApi {
  public partial class Contract {
    public string Instrument => ComboLegsToString().IfEmpty((LocalSymbol?.Replace(".", "") + "").ToUpper());

    public static readonly ConcurrentDictionary<string, Contract> Contracts = new ConcurrentDictionary<string, Contract>(StringComparer.OrdinalIgnoreCase);
    public override string ToString() =>
      ComboLegsToString().IfEmpty($"{LocalSymbol ?? Symbol} {SecType}");// {Exchange} {Currency}";

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
    public static string Key(this Contract that) {
      if(that.IsOption()) return that.LocalSymbol;
      if(that.ComboLegs?.Any() == true) {
        string legs = that.ComboLegsToString();
        return $"{ that}<>{legs}";
      }
      return that.Instrument;
    }
  }

}
