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

namespace ConsoleApp {
  static class EdgeTests {
    public static void MoveEdge(AccountManager am) {
      var instrument = "ESU0";
      bool isCall = false;
      int quantity = -1;
      int daysToSkip = 0;
      int edgeoffset = 15;
      double edgeShift = isCall ? edgeoffset : -edgeoffset;
      double takeProfitPoints = 0;
      var defCan = ErrorMessage.Empty(new OrderContractHolder[0].AsEnumerable());

      (from underContract in instrument.ReqContractDetailsCached().Select(cd => cd.Contract)
       from underPrice in underContract.ReqPriceSafe()
       from openCurrent in am.OpenEdgeOrder(instrument, isCall, quantity, daysToSkip, underPrice.avg - 1, 20)
       from edgeParams in am.OpenEdgeOrderParams(instrument, isCall, quantity, daysToSkip, underPrice.avg + edgeShift, takeProfitPoints)
       let currentOrders = FindOtherEdgeOrders(am, edgeParams.contract).ToList().ThrowIf(currentOrders => currentOrders.IsEmpty())
       from ps in currentOrders.Select(currentOrder => am.CancelOrder(currentOrder.order.OrderId)).Merge().ToArray()
       let canceled = ps.Any(p=> !p.error.HasError ? false : throw GenericException.Create(new { context = p.value.Flatter(","), p.error }))
       select new { edgeParams }
       )
       .ToArray()
       .Subscribe(eps => Program.HandleMessage(eps.ToTextOrTable("Move Edge")), exc => Program.HandleMessage(exc));
    }
    public static IEnumerable<AccountManager.OrderContractHolder> FindOtherEdgeOrders(AccountManager am, Contract contract) {
      var edgeOrders = am.UseOrderContracts(ocs => ocs.Where(oc =>
        oc.order.IsSell &&
        !oc.isDone &&
        oc.contract.IsOption &&
        oc.contract.IsCall == contract.IsCall &&
        oc.contract.Symbol == contract.Symbol &&
        (contract.IsCall ? oc.contract.Strike < contract.Strike : oc.contract.Strike > contract.Strike) &&
        oc.order.HasPriceCodition
      )).Concat();
      return edgeOrders;
      //edgeOrders.ThrowIf(() => edgeOrders.IsEmpty());

    }
  }
}
