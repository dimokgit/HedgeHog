using HedgeHog;
using HedgeHog.Core;
using HedgeHog.Shared;
using IBApi;
using IBApp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static IBApp.AccountManager;
using static ConsoleApp.Program;
using MarkdownLog;

namespace ConsoleApp {
  static partial class PositionsTest {
    public static void RollAuto(AccountManager am) {
      var a = MonoidsCore.ToFunc((ComboTrade ct) => new { ct.contract, ct.Change, ct.change, ct.pl, ct.openPrice, ct.price, ct.position, ct.underPrice });
      Positioner(am, c => true)
        .Do(positions => { HandleMessage(am.Positions.ToTextOrTable("All Positions:")); })
        .SelectMany(_ => am.ComboTrades(1).Where(c => !c.contract.IsBag).ToArray())
        .Subscribe(trades => {
          HandleMessage(trades.Select(a).ToTextOrTable("Current Trade"), false);
          am.CurrentRollOverByUnder("ESU0", 1, 3, 1, 0)
          .OrderByDescending(r => r.dpw)
          .ToList()
          .Subscribe(cs => HandleMessage(cs.ToTextOrTable(), false));
        });
    }
    public static void RollAutoOpen(AccountManager am) {
      var a = MonoidsCore.ToFunc((ComboTrade ct) => new { ct.contract, ct.Change, ct.change, ct.pl, ct.openPrice, ct.price, ct.position, ct.underPrice });
      var rollQuantity = 2;
      (from pos in Positioner(am, c => true).Do(positions => { HandleMessage(am.Positions.ToTextOrTable("All Positions:")); })
       from trade in am.ComboTrades(1).Where(c => !c.contract.IsBag).Where(t => t.pl < 0).Take(1)
       from under in trade.contract.UnderContract
       from ro in am.CurrentRollOverByUnder(under, rollQuantity, 1, 1, 0).OrderByDescending(r => r.dpw).Take(1)
       from oc in am.OpenRollTrade(trade.contract.LocalSymbol, ro.roll.LocalSymbol, rollQuantity, true)
       select oc
       ).Subscribe();
    }

    private static IObservable<Price> Pricer() =>
      IBClientCore.IBClientCoreMaster.PriceChangeObservable.Select(p => p.EventArgs.Price)
      .DistinctUntilChanged(p => (p.Ask, p.Bid)).Do(price => { HandleMessage(price); });

    public static void RollsUnder(AccountManager am) {
      var positioner = Positioner(am, c => c.Contract.LocalSymbol == "ESU0");
      positioner
        .SelectMany(_ => am.ComboTrades(1).ToArray()
        )
        .Select(trades => {
          var under = trades.Where(ct => ct.contract.LocalSymbol == "ESU0").Single();
          var coveredPut = under.CoveredOption(trades).Single();
          return new { under, coveredPut };
        })
      .Subscribe(rollContext => {
        var a = MonoidsCore.ToFunc((ComboTrade ct) => new { ct.contract, ct.Change, ct.change, ct.pl, ct.position });
        var trade = rollContext.under;
        var cover = rollContext.coveredPut;
        //HandleMessage(trade.ToTextTable("Covered Put"), false);
        HandleMessage(new[] { a(trade), a(cover) }.ToTextOrTable("Roll Trade"), false);
        am.CurrentRollOver(trade.contract.LocalSymbol, false, 2, 2, 0)
        .ToArray()
        .Subscribe(roll => HandleMessage(roll.Select(r => new {
          r.roll, days = r.days.ToString("00"), bid = r.bid.ToString("0.00"), perc = r.perc.ToString("0.0"), dpw = r.dpw.ToInt(), r.ppw, r.amount, r.delta
        }).ToTextOrTable("Roll Overs")));
        //new { roll.roll, days = roll.days.ToString("00"), bid = roll.bid.ToString("0.00"), perc = roll.perc.ToString("0.0"), dpw = roll.dpw.ToInt(), roll.ppw, roll.amount, roll.delta }
      });
    }

    private static IObservable<PositionMessage> Positioner(AccountManager am, Func<PositionMessage, bool> where) => am.PositionsObservable
            .Where(where)
            .DistinctUntilChanged(c => c.Contract.Key)
            .Do(positions => { HandleMessage(am.Positions.ToTextOrTable("All Positions:")); });

    public static void ComboTrades(AccountManager am) {
      am.PositionsObservable.Do(positions => {
        HandleMessage(am.Positions.ToTextOrTable("All Positions:"));
      }).Skip(1)
      .Where(_ => am.Positions.Count > 1)
      .SelectMany(_ =>
        am.ComboTrades(1)
        .ToArray()
        )
      .Subscribe(comboPrices => {
        var swCombo = Stopwatch.StartNew();
        HandleMessage2("Matches: Start");
        HandleMessage(comboPrices.Select(c => new { c.contract, c.position, c.openPrice, c.closePrice }).ToTextOrTable(), false);
        HandleMessage2($"Matches: Done in {swCombo.ElapsedMilliseconds} ms =========================================");
      });
    }
  }
}
