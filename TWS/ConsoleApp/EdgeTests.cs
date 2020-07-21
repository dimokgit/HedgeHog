using HedgeHog;
using HedgeHog.Core;
using HedgeHog.Shared;
using IBApi;
using IBApp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static IBApp.AccountManager;
using static ConsoleApp.Program;
namespace ConsoleApp {
  static class EdgeTests {
    public static void MoveEdge(AccountManager am) {
      var instrument = "ESU0";
      int quantity = -1;
      int daysToSkip = 0;
      int edgeoffset = 10;
      double edgeShift(bool isCall)=> isCall? edgeoffset : -edgeoffset;
      double takeProfitPoints = 20;
      var defCan = ErrorMessage.Empty(new OrderContractHolder[0].AsEnumerable());

      // Create test edge orders
      (from tp in TestParams(instrument)
       //where false
       from edgeCall in am.OpenEdgeOrder(instrument, true, quantity, daysToSkip, tp.underPrice.ask + edgeShift(true), takeProfitPoints)
       from edgePut in am.OpenEdgeOrder(instrument, false, quantity, daysToSkip, tp.underPrice.ask+edgeShift(false), takeProfitPoints)
       select edgeCall.Concat(edgePut)
       )
       .Subscribe(eps => HandleMessage(eps.OrderBy(h => h.holder.OrderId).Select(holder => new { holder }).ToTextOrTable("Move Edge")), exc => HandleMessage(exc));

      var pricer = IBClientCore.IBClientCoreMaster.PriceChangeObservable
      .Select(p => p.EventArgs.Price)
      .Where(p => p.Pair == instrument)
      .DistinctUntilChanged(p => (p.Ask, p.Bid))
      .Sample(1.FromSeconds());

      var parameter = (
        from pi in pricer.Select((p, i) => (p, i))
        let p = pi.p
        from isCall in new []{true,false }
        let edge = p.Bid + edgeShift(isCall) + (pi.i / 10.0) * edgeShift(isCall).Sign()
        from param in am.OpenEdgeOrderParams(p.Pair, isCall, 0, edge)        
        select new { param, edge }
        )
        .Do(p => HandleMessage(new { p.edge, p.param.contract, p.param.currentPrice, replaceOrders = p.param.replaceOrders.Flatter(",") }.ToTextTable(), false))
        .Where(p=> p.param.replaceOrders.Any());

      (from par in parameter
       .Do(p => Program.HandleMessage($"Replacing {p.param.replaceOrders.Flatter("")} with {p.param.contract}"))
       from och in am.OpenEdgeOrder(o => { }, par.param, quantity, takeProfitPoints)
       select och
       ).Subscribe(ochs =>HandleMessage(ochs.Select(och=>new {och.holder,och.error }).ToTextTable()));

    }
    public static IObservable<IEnumerable<OrderContractHolderWithError>> OpenEdgeCallPut(AccountManager am) {
      var instrument = "ESU0";
      int quantity = -1;
      int daysToSkip = 0;
      int edgeoffset = 10;
      double edgeShift(bool isCall) => isCall ? edgeoffset : -edgeoffset;
      double takeProfitPoints = 20;

      // Create test edge orders
      return (from tp in TestParams(instrument)
       from edgeCall in am.OpenEdgeOrder(instrument, true, quantity, daysToSkip, tp.underPrice.ask + edgeShift(true), takeProfitPoints)
       from edgePut in am.OpenEdgeOrder(instrument, false, quantity, daysToSkip, tp.underPrice.ask + edgeShift(false), takeProfitPoints)
       select edgeCall.Concat(edgePut)
       )
       .Do(eps => HandleMessage(eps.OrderBy(h => h.holder.OrderId).Select(holder => new { holder }).ToTextOrTable("Move Edge")), exc => HandleMessage(exc));
    }

    private static IObservable<(string instrument, MarketPrice underPrice)> TestParams(string instrument) => 
      from underContract in instrument.ReqContractDetailsCached().Select(cd => cd.Contract)
      from underPrice in underContract.ReqPriceSafe()
      select (instrument, underPrice);
    /*
var instrument = "ESU0";
var isCall = false;
var quantity = 1;
// open "existing order
(from mp in instrument.ReqContractDetailsCached().SelectMany(cd => cd.Contract.ReqPriceSafe())
from o in am.OpenEdgeOrder(instrument, isCall, -quantity, 0, mp.bid-10, 100)
select o
).Subscribe(os => os.ForEach(o => HandleMessage(o.ToAnon())));

var pricer = IBClientCore.IBClientCoreMaster.PriceChangeObservable
.Select(p => p.EventArgs.Price)
.Where(p => p.Pair == instrument)
.DistinctUntilChanged(p => (p.Ask, p.Bid))
.Sample(1.FromSeconds());
(from p in pricer
let edge = p.Bid-3
from edgePrams in am.OpenEdgeOrderParams(p.Pair, isCall, 0,  edge)
select edgePrams
)
.Subscribe(p => {
HandleMessage(new { p.contract, p.limitPrice, orders = p.currentOrders.Flatter(",") });
});

*/
  }
  }
