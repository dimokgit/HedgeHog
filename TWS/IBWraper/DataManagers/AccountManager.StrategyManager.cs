using HedgeHog;
using IBApi;
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace IBApp {
  public partial class AccountManager {
    const string STRATEGY_PREFIX = "TM:";
    public readonly Subject<(string instrument, double level, bool isCall, int quantity, double profit)[]> OrderEnrtyLevel = new Subject<(string instrument, double level, bool isCall, int quantity,double profit)[]>();
    bool strategyTest = false;
    void WireOrderEntryLevels() {
      var reqs = (from oels in OrderEnrtyLevel
                  from oel in oels
                  from options in OptionsToSell(oel.instrument, oel.level, isCall: oel.isCall)
                  select new { options, oel.quantity, oel.isCall,oel.profit }
                 );
      var b = reqs.Where(r => r.isCall);//.DistinctUntilChanged(_ => DateTime.Now.RoundBySeconds(strategyTest ? 10 : 1));
      var s = reqs.Where(r => !r.isCall);//.DistinctUntilChanged(_ => DateTime.Now.RoundBySeconds(strategyTest ? 10 : 1));
      (from r in b.Merge(s)
       from t in r.options
       from under in t.option.UnderContract
       from underPrice in under.ReqPriceSafe()
         //let isBuy = r.quantity>0
       let contract = t.option
       let isCall = contract.IsCall
       let isMore = !isCall
       let level = t.level
       let profit = r.profit
       from hasPos in Positions.Where(p
       => p.position != 0
       && p.contract.UnderContract.SequenceEqual(contract.UnderContract)
       && p.contract.IsCall == contract.IsCall
       && p.contract.Strike.Abs(contract.Strike) <= 5
       ).Select(_ => true).DefaultIfEmpty()
       where !hasPos
       where isCall && underPrice.ask > level || !isCall && underPrice.bid < level
       let openCond = under.PriceCondition(level.Round(2), isMore)
       let tpCond = profit < 1 ? null : under.PriceCondition((level + profit * (contract.IsCall ? 1 : -1) * r.quantity.Sign()).Round(2), isMore)
       let strategyRef = STRATEGY_PREFIX + t.option.Right
       from hc in OrderContractsInternal.Values.Where(h => h.order.OrderRef == strategyRef).Select(h => h.order.OrderId).DefaultIfEmpty()
       from co in hc != 0 ? CancelOrder(hc) : Observable.Return(new ErrorMessages<OrderContractHolder>(default, default))
       where co.error.reqId == 0 || !co.error.HasError
       from ot in OpenTrade(t.option, r.quantity, 0, tpCond == null ? profit : 0, true, DateTime.Now.AddMinutes(10), strategyTest ? IbClient.ServerTime.AddHours(1) : default, openCond, tpCond, strategyRef)
       select ot
       ).Subscribe();
    }
    public IObservable<(double level, Contract option)[]> OptionsByBS(string pair, double level, bool isCall) {
      var expSkip = IbClient.ServerTime.Hour > 16 ? 1 : 0;
      var options = from os in (isCall ? Calls() : Puts()).ToArray()
                    from option in os.OrderBy(OrderBy).Take(3).TakeLast(1)
                    select (level, option);
      return options.ToArray();

      IObservable<Contract> Calls() =>
        CurrentOptions(pair, level, expSkip, 4, c => c.IsCall && c.Strike < level).SelectMany(a => a.Select(t => t.option));
      IObservable<Contract> Puts() =>
        CurrentOptions(pair, level, expSkip, 4, c => c.IsPut && c.Strike > level).SelectMany(a => a.Select(t => t.option));
      double OrderBy(Contract c) => c.IsCall ? 1 / c.Strike : c.Strike;
    }
    public IObservable<(double level, Contract option)[]> OptionsToSell(string pair, double level, bool isCall) {
      var expSkip = IbClient.ServerTime.Hour > 16 ? 2 : 1;
      var options = from os in (isCall ? Calls() : Puts()).ToArray()
                    from option in os.OrderBy(OrderBy).Take(1).TakeLast(1)
                    select (level, option);
      return options.ToArray();

      IObservable<Contract> Calls() =>
        CurrentOptions(pair, level, expSkip, 4, c => c.IsCall && c.Strike > level).SelectMany(a => a.Select(t => t.option));
      IObservable<Contract> Puts() =>
        CurrentOptions(pair, level, expSkip, 4, c => c.IsPut && c.Strike < level).SelectMany(a => a.Select(t => t.option));
      double OrderBy(Contract c) => c.IsPut ? 1 / c.Strike : c.Strike;
    }
  }
}
