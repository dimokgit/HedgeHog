using HedgeHog;
using IBApi;
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace IBApp {
  public partial class AccountManager {
    //https://stackoverflow.com/questions/11010602/with-rx-how-do-i-ignore-all-except-the-latest-value-when-my-subscribe-method-is
    public readonly Subject<(string instrument, double level, bool isCall, int quantity, double profit)[]> TrendEdgeLevel = new Subject<(string instrument, double level, bool isCall, int quantity, double profit)[]>();
    public DateTime TrendEdgesLastDate;
    bool _WireTrendEdgesTest = false;
    void WireTrendEdges() {
      var parameter = (
        from tls in TrendEdgeLevel.DistinctUntilChanged(ts=>ts.Average(t=>t.level.Round(2))).Sample(1.FromSeconds())
        from tl in tls
        from param in OpenEdgeOrderParams(tl.instrument, tl.isCall, 0, tl.level)
        select new { param, tl }
        )
        .Retry()
        .Do(p => {
          TrendEdgesLastDate = DateTime.Now;
          TraceIf(_WireTrendEdgesTest, $"EdgeParams {new { p.param.contract, edge = p.tl.level.Round(2), current = p.param.currentOrders.Flatter("") }}");
        })
        .Where(p => p.param.replaceOrders.Any());

      (from par in parameter
       .Do(p => Trace($"Replacing {p.param.replaceOrders.Flatter("")}"))
       from och in OpenEdgeOrder(o => { }, par.param, -par.tl.quantity, par.tl.profit)
       select och
       ).Subscribe();
    }
  }
}
