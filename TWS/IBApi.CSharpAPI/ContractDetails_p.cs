using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog;
using System.Diagnostics;
using System.Globalization;

namespace IBApi {
  partial class ContractDetails {
    public static readonly ConcurrentDictionary<string, ContractDetails> ContractDetailsCache = new ConcurrentDictionary<string, ContractDetails>(StringComparer.OrdinalIgnoreCase);
    public static void ClearCache() { ContractDetailsCache.Clear(); }
    static object _gate = new object();
    public ContractDetails AddToCache() {
      lock(_gate) {
        //if(Contract.IsBag && Debugger.IsAttached) { Debugger.Break(); }
        if(!Contract.IsBag && Contract.ConId == 0 && Debugger.IsAttached)
          Debugger.Break();
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
    DateTime[][] _TradingTimes = new DateTime[0][] { };
    public DateTime[][] TradingTimes {
      get {
        if(_TradingTimes.Length > 0) return _TradingTimes;
        var timeStr = (from thss in (TradingHours ?? "").Split(';')
                      let ths = thss.Split('-')
                      where ths.Length == 2
                      select ths.Select(th =>
                      DateTime.ParseExact(th, "yyyyMMdd:HHmm", CultureInfo.InvariantCulture).ChangeTwsZone(TimeZoneId)).ToArray()
                      ).ToList();
        if(timeStr.Count == 4) {
          var dl = timeStr.Last().Last().Date.AddDays(1);
          timeStr.Add(new[] { dl + 4.0.FromHours(), dl + 20.0.FromHours() });
        }
        return _TradingTimes = timeStr.ToArray();
      }
    }
    public bool IsTradeHour(DateTime td) => isTradeHour(TradingTimes, td);
    private static bool isTradeHour(DateTime[][] dates, DateTime td) {
      return (from dp in dates
              where dp.Any(d => d.DayOfWeek == td.DayOfWeek)
              from cd in dp.Where(d => d.DayOfWeek == td.DayOfWeek).Select(d => d.CloneDate(td))
              where cd.Between(dp)
              select td
       ).Any();
    }
    public bool IsTradingHours(DateTime date) => TradingTimes.Any(t => date.Between(t[0], t[1]));
    public TimeSpan TradingTimePerWeek =>TradingTimes.Aggregate(0.FromMinutes(), (p, c) => p + c.Pairwise((st, et) => et - st).Single());

    public string MarketNameSafe => Contract.IsStock ? Contract.Symbol : MarketName.IfEmpty(Contract.Symbol);
    public override string ToString() =>
      base.ToString();
  }
}
