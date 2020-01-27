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
  static class Tests {
    public static void HedgeCombo(AccountManager am) {
      am.PositionsObservable.Subscribe(positions => Program.HandleMessage(am.Positions.ToTextOrTable("All Positions:")));

      {
        var parentContract = "spy".ContractFactory();
        var hedgeContract = "vxx".ContractFactory();
        var quantityParent = 600;
        var r = quantityParent / ((quantityParent / 2.26).Round(0) + 1);
        Func<(double p1, double p2)> hp = () => r.PositionsFromRatio();
        while(new[] { (hp().p1 * 600).ToInt(), (hp().p2 * 600).ToInt() }.GCD() != 1)
          r += 0.01;
        var isTest = true;
        (from hc in AccountManager.MakeHedgeComboSafe(quantityParent, parentContract, hedgeContract, hp().p1, -hp().p2, false)
         from p in hc.contract.ReqPriceSafe().Select(ab => quantityParent > 0 ? ab.ask : ab.bid)
         from ot in am.OpenTrade(o => o.Transmit = !isTest, hc.contract, hc.quantity, p)
         select ot
         )
         .Subscribe(c => {
           Program.HandleMessage(c.Select(t => new { t.holder, t.error }).ToTextOrTable("Test Order:"));
           Program.HandleMessage(am.OrderContractsInternal.Items.Select(t => new { t.order, t.contract }).ToTextOrTable("Test Order Holders:"));
         });
        return;
      }
      (from pos in am.PositionsObservable.Take(2).ToArray()
       from ct in am.ComboTrades(1)
       where ct.contract.IsBag
       from p in ct.contract.ReqPriceSafe().Select(ab=>ab.Price(true))
       from ots in am.OpenTrade(o=> o.Transmit=false , ct.contract, -ct.position)
       from ot in ots
       select ot
      ).Subscribe(oc => Program.HandleMessage(oc));
      return;

      var h1 = "SPY";
      var h2 = "QQQ";
      var maxLegQuantity = 15;
      {
        AccountManager.MakeHedgeComboSafe(maxLegQuantity, h1, h2, 1, 1, false)
          .ToArray()
          .Subscribe(hs => Program.HandleMessage(hs.Select(h => new { h.contract, h.quantity }).ToTextTable("Hedge Combo")));
        return;
      }
      {
        var tvDays = 3;
        var posCorr = true;
        am.CurrentHedges(h1, h2, tvDays, posCorr)
        .Subscribe(hh0 => {
          if(hh0.Count == 0) {
            Program.HandleMessage("**** No hedges ****");
            return;
          }
          Program.HandleMessage(hh0.SelectMany(h => h.options.Select(option => new { h.contract, h.isBuy, option, h.ratio, amount = h.price * h.ratio, h.context })).ToTextOrTable("Hedge"));
          am.CurrentHedges(h1, h2, "", c => c.ShortWithDate2, tvDays, posCorr)
          .Subscribe(hh2 => {
            if(hh2.IsEmpty()) {
              Program.HandleMessage("**** CurrentHedges empty****");
            } else {
              Program.HandleMessage(hh2.SelectMany(h => h.options.Select(option => new { h.contract, h.isBuy, option, h.ratio, h.price, h.context })).ToTextOrTable("Hedge 2"));
              { // Old style for futures
                var combo = AccountManager.MakeHedgeCombo(maxLegQuantity, hh2[0].contract, hh2[1].contract, hh2[0].ratio, hh2[1].ratio).With(c => new { combo = c, context = hh2.ToArray(t => t.context).MashDiffs() });
                var a = new { combo.combo.contract.ShortString, combo.combo.contract.DateWithShort, combo.combo.contract.ShortWithDate, combo.combo.contract, combo.context };
                Program.HandleMessage($"{a.ToTextTable("Hedge Combo:")}");
                (from p in combo.combo.contract.ReqPriceSafe() select new { combo.combo.contract, p.bid, p.ask }).Subscribe(Program.HandleMessage);
              }
              { // New style for Options
                var combos = hh2.CurrentOptionHedges(maxLegQuantity, Program.HandleMessage);
                var a = combos.Select(c => new { buy = c.buy.ShortSmart, sell = c.sell.ShortSmart, c.quantity });

                Program.HandleMessage($"{a.ToTextOrTable("Hedge Option Combos:")}");
                (from combo in combos.ToObservable()
                 from p in combo.buy.ReqPriceSafe()
                 select new { combo, p = new { combo.buy.ShortSmart, p.bid, p.ask } }).Subscribe(p => {
                   Program.HandleMessage(p.p);
                   OpenTrade(p.combo, true);
                   OpenTrade(p.combo, false);
                 });

                void OpenTrade((Contract buy, Contract sell, int quantity) optionHedge, bool buy) {
                  var pos = 1;
                  if(pos == 1) {
                    //am.OpenTrade(combo.combo.contract, combo.combo.quantity * pos)
                    //.Subscribe(orderHolder => {
                    //  HandleMessage(orderHolder.ToTextOrTable());
                    //});
                    am.OpenTrade(o => o.Transmit = false, buy ? optionHedge.buy : optionHedge.sell, optionHedge.quantity)
                    .SelectMany(ohs => ohs.Select(oh => new { oh.holder, oh.error }))
                    .ToArray()
                    //.Do(orderHolder => { Program.HandleMessage(orderHolder.ToTextOrTable("Hedge Order")); })
                    .Subscribe();
                  }
                }
              }
            }
          });

        });
        return;
      }
      am.CurrentOptions(h1, double.NaN, 0, 10, c => true)
      .Subscribe(os => Program.HandleMessage(os.Select(o => new { o.option, o.bid, o.ask }).ToArray().ToTextOrTable("Options:")));


      (from o in am.OpenOrderObservable
       from p in o.Contract.ReqPriceSafe(5)
       select new { o, p }
       ).Subscribe(x => Program.HandleMessage(new { x.o.Contract, x.o.Order, x.o.OrderState, x.p }.ToTextTable("Open Trade")));

      (from s in new[] { h1, h2 }.ToObservable().Take(0)
       from cd in DataManager.IBClientMaster.ReqContractDetailsCached(s)
       from p in cd.Contract.ReqPriceSafe()
       select new { cd.Contract, p }
       ).Subscribe(Program.HandleMessage);

      am.PositionsObservable.SkipWhile(_ => am.Positions.Count < 2).Subscribe(ops => {
        Program.HandleMessage("Closing combo trade:~" + Thread.CurrentThread.ManagedThreadId + Thread.CurrentThread.Name);
        (from ct in am.ComboTrades(5)
         from p in ct.contract.ReqPriceSafe(3).Do(_ => { Thread.Sleep(10000); })
         select new { ct, p }
         )
        .ToArray()
        .Subscribe(posHedges => {
          Program.HandleMessage(posHedges.Select(h => new { h.ct.contract.ShortString, h.ct.open, h.ct.close, h.ct.pl, h.ct.closePrice, h.p }).ToTextOrTable("Positions:"));
          var ct = posHedges.Select(p => p.ct).SingleOrDefault(p => p.contract.IsHedgeCombo);
          if(ct == null) Program.HandleMessage("No hedged positions found.");
          else {
            return;
            am.OpenTrade(o => o.Transmit = false, ct.contract, -ct.position)
            .Subscribe(orderHolder => { Program.HandleMessage(orderHolder.ToTextOrTable()); });
            var combo = AccountManager.MakeHedgeCombo(maxLegQuantity, Contract.FromCache(h1).Single(), Contract.FromCache(h2).Single(), 1, 0.6).With(c => new { c.contract, c.quantity });
            var j2 = combo.contract.ToJson(true);
            var pos = -1;
            if(pos == -11)
              am.OpenTrade(o => o.Transmit = false, combo.contract, combo.quantity * pos)
              .Subscribe(orderHolder => { Program.HandleMessage(orderHolder.ToTextOrTable()); });
          }
        });

      });
      //return;
    }


    public static void CurrentOptionsTest(AccountManager am, string symbol) {
      am.CurrentOptions(symbol, double.NaN, 0, 2, c => true)
      .Subscribe(ss => Program.HandleMessage(ss.Select(a => a.option).Select(c => new { c.ShortString, c.DateWithShort, c.ShortWithDate2 }).ToTextOrTable("Options:")));
      am.CurrentStraddles(symbol, double.NaN, 0, 2, 1)
      .Subscribe(ss => Program.HandleMessage(ss.Select(a => a.combo.contract).Select(c => new { c.ShortString, c.DateWithShort, c.ShortWithDate2 }).ToTextOrTable("Straddles:")));
    }
  }
}
