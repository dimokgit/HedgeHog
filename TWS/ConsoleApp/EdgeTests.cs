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
      double takeProfitPoints = 20;
      var defCan = ErrorMessage.Empty(new OrderContractHolder[0].AsEnumerable());

      var testParams = from underContract in instrument.ReqContractDetailsCached().Select(cd => cd.Contract)
                       from underPrice in underContract.ReqPriceSafe()
                       select new { underPrice };

      (from tp in testParams
       from openCurrent in am.OpenEdgeOrder(instrument, isCall, quantity, daysToSkip, tp.underPrice.avg + edgeShift / 3, takeProfitPoints)
       from edgeOrder in am.OpenEdgeOrder(instrument, isCall, quantity, daysToSkip, tp.underPrice.avg + edgeShift, takeProfitPoints)
       select edgeOrder
       )
       .Subscribe(eps => Program.HandleMessage(eps.OrderBy(h=>h.holder.OrderId).Select(holder => new { holder}).ToTextOrTable("Move Edge")), exc => Program.HandleMessage(exc));
    }
  }
}
