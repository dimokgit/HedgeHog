using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IBApi {
  partial class ContractDetails {
    static readonly ConcurrentDictionary<string, ContractDetails> ContractDetailsCache = new ConcurrentDictionary<string, ContractDetails>(StringComparer.OrdinalIgnoreCase);
    public ContractDetails AddToCache() { ContractDetailsCache.TryAdd(summary.AddToCache().Key, this); return this; }
    public IEnumerable<ContractDetails> FromCache() => FromCache(summary);
    public static IEnumerable<ContractDetails> FromCache(Contract contract) => FromCache(contract.Key);
    public static IEnumerable<ContractDetails> FromCache(string instrument) {
      if(ContractDetailsCache.TryGetValue(instrument, out var contract))
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
