using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using HedgeHog.Bars;
using System.Collections.Concurrent;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    private IList<int> MACrosses(IList<Rate> rates, int frame) {
      var rates1 = rates.Zip(rates.Skip(1), (f, s) => new { f = f.PriceAvg, s = s.PriceAvg, ma = f.PriceCMALast }).ToArray();
      var crosses = new int[0].ToConcurrentQueue();
      Partitioner.Create(Enumerable.Range(0, rates.Count - frame).ToArray(), true).AsParallel()
        .ForAll(i => {
          var rates2 = rates1.ToArray(frame);
          Array.Copy(rates1, i, rates2, 0, frame);
          crosses.Enqueue(rates2.Count(r => r.f.Sign(r.ma) != r.s.Sign(r.ma)));
        });
      return crosses.ToArray();
    }
  }
}
