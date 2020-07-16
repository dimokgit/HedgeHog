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

namespace ConsoleApp {
  static class EdgeTests {
    public static void MoveEdge(AccountManager am) {
      var instrument = "ESU0";
      bool isCall = true;
      int quantity = 1;
      int daysToSkip = 0;
      double edgeShift = 0;
      double takeProfitPoints = 0;
      (from underContract in instrument.ReqContractDetailsCached().Select(cd=>cd.Contract)
       from underPrice in underContract.ReqPriceSafe()
       from currents in am.CurrentOptions(instrument, underPrice.avg, 0, 2, o => o.IsCall == isCall)
       from current in currents.Take(1).Select(c=>c.option)
       from edgeParams in am.OpenEdgeOrderParams(instrument,isCall,quantity,daysToSkip,underPrice.avg+edgeShift,takeProfitPoints)
       select new { edgeParams, current }
       )
       .ToArray()
       .Subscribe(eps=>Program.HandleMessage(eps.ToTextOrTable("Move Edge")));
    }
  }
}
