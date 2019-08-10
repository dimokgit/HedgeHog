using HedgeHog;
using HedgeHog.Core;
using IBApi;
using IBApp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp {
  static class Tests {
    public static void HedgeCombo(AccountManager am) {
      var h1 = "ESU9";
      var h2 = "NQU9";
      var maxLegQuantity = 10;
      //am.CurrentOptions(h1, double.NaN, 0, 10,c=>true)
      //.Subscribe(os => HandleMessage(os.Select(o => new { o.option }).ToArray().ToTextOrTable("Options:")));
      (from s in new[] { h1, h2 }.ToObservable()
       from cd in DataManager.IBClientMaster.ReqContractDetailsCached(s)
       from p in DataManager.IBClientMaster.ReqPriceSafe(cd.Contract)
       select new { cd.Contract, p }
       ).Subscribe(Program.HandleMessage);

      am.PositionsObservable.Spy().DistinctUntilChanged(pm=>pm.ToString()).Take(2).ToArray().Subscribe(ops => {
        Program.HandleMessage("Closing combo trade:~"+Thread.CurrentThread.ManagedThreadId+Thread.CurrentThread.Name);
        am.ComboTrades(5).ToArray().Subscribe(posHedges => {
          Program.HandleMessage(posHedges.Select(h => new { h.contract.ShortString, h.open, h.close, h.pl, h.closePrice }).ToTextOrTable("Positions:"));
          return;
          var ct = posHedges.Single(p => p.contract.IsFuturesCombo);
          am.OpenTrade(o => o.Transmit = false, ct.contract, -ct.position)
          .Subscribe(orderHolder => { Program.HandleMessage(orderHolder.ToTextOrTable()); });
          var combo = AccountManager.MakeHedgeCombo(maxLegQuantity, Contract.FromCache(h1).Single(), Contract.FromCache(h2).Single(), 1, 0.6).With(c => new { c.contract, c.quantity });
          var j2 = combo.contract.ToJson(true);
          var pos = -1;
          if(pos == -1)
            am.OpenTrade(o => o.Transmit = false, combo.contract, combo.quantity * pos)
            .Subscribe(orderHolder => { Program.HandleMessage(orderHolder.ToTextOrTable()); });
        });

      });
      return;
      am.CurrentHedges(h1, h2)
        .ToArray()
      .Subscribe(hh0 => {
        if(hh0.Length == 0) {
          Program.HandleMessage("**** No hedges ****");
          return;
        }
        var hh = hh0[0];
        Program.HandleMessage(hh.Select(h => new { h.contract, h.ratio, amount = h.price * h.ratio, h.context }).ToTextOrTable("Hedge"));
        am.CurrentHedges(h1, h2, "", c => c.ShortWithDate2)
        .Subscribe(hh2 => {
          if(hh2.IsEmpty()) {
            Program.HandleMessage("**** CurrentHedges empty****");
          } else {
            Program.HandleMessage(hh2.Select(h => new { h.contract, h.ratio, amount = h.price * h.ratio, h.context }).ToTextOrTable("Hedge 2"));
            var combo = AccountManager.MakeHedgeCombo(maxLegQuantity, hh2[0].contract, hh2[1].contract, hh2[0].ratio, hh2[1].ratio).With(c => new { combo = c, context = hh2.ToArray(t => t.context).MashDiffs() });
            var a = new[] { new { combo.combo.contract.ShortString, combo.combo.contract.DateWithShort, combo.combo.contract.ShortWithDate, combo.combo.contract, combo.context } };
            Program.HandleMessage($"{a.ToTextOrTable("Hedge Combo:")}");
            (from p in DataManager.IBClientMaster.ReqPriceSafe(combo.combo.contract) select new { combo.combo.contract, p.bid, p.ask }).Subscribe(Program.HandleMessage);
            var pos = -1;
            if(pos == 11) {
              //am.OpenTrade(combo.combo.contract, combo.combo.quantity * pos)
              //.Subscribe(orderHolder => {
              //  HandleMessage(orderHolder.ToTextOrTable());
              //});
              am.OpenTrade(o => o.Transmit = false, combo.combo.contract, combo.combo.quantity * -pos)
              .Subscribe(orderHolder => { Program.HandleMessage(orderHolder.ToTextOrTable()); });
            }
          }
        });

      });

    }

    public static void CurrentOptionsTest(AccountManager am, string symbol) {
      am.CurrentOptions(symbol, double.NaN, 0, 2, c => true)
      .Subscribe(ss => Program.HandleMessage(ss.Select(a => a.option).Select(c => new { c.ShortString, c.DateWithShort, c.ShortWithDate2 }).ToTextOrTable("Options:")));
      am.CurrentStraddles(symbol, double.NaN, 0, 2, 1)
      .Subscribe(ss => Program.HandleMessage(ss.Select(a => a.combo.contract).Select(c => new { c.ShortString, c.DateWithShort, c.ShortWithDate2 }).ToTextOrTable("Straddles:")));
    }
  }
}
