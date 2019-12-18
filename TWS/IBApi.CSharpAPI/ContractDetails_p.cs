using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog;
using System.Diagnostics;

namespace IBApi {
  partial class ContractDetails {
    static readonly ConcurrentDictionary<string, ContractDetails> ContractDetailsCache = new ConcurrentDictionary<string, ContractDetails>(StringComparer.OrdinalIgnoreCase);
    public static void ClearCache() { ContractDetailsCache.Clear(); }
    static object _gate = new object();
    public ContractDetails AddToCache() {
      lock(_gate) {
        //if(Contract.IsHedgeCombo) { Debugger.Break(); }
        ContractDetailsCache.TryAdd(contract.AddToCache().Key, this);
        return this;
      }
    }
    public IEnumerable<ContractDetails> FromCache() => FromCache(contract);
    public static IEnumerable<ContractDetails> FromCache(Contract contract)
      => FromCache(contract.Key).Concat(FromCache(contract, (cache, c) => cache.ConId == c.ConId)).Take(1);
    public static IEnumerable<ContractDetails> FromCache(Contract contract, Func<Contract, Contract, bool> where)
      => ContractDetailsCache.Where(cd => where(cd.Value.contract, contract)).Select(cd => cd.Value);
    public static IEnumerable<ContractDetails> FromCache(string key) {
      if(key.IsNullOrWhiteSpace())
        throw new Exception("ContractDetailsKey is empty");
      if(ContractDetailsCache.TryGetValue(key, out var contract))
        yield return contract;
    }
    public static IEnumerable<T> FromCache<T>(string instrument, Func<ContractDetails, T> map) {
      if(ContractDetailsCache.TryGetValue(instrument, out var contract))
        yield return map(contract);
    }
    public override string ToString() =>
      base.ToString();
  }
}
