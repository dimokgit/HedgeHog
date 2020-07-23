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

namespace ConsoleApp {
  static partial class PositionsTest {
    public static void Rolls(AccountManager am) {
      am.PositionsObservable
        .Where(c => c.Contract.LocalSymbol == "ESU0")
        .DistinctUntilChanged(c => c.Contract.Key)
        .Do(positions => { HandleMessage(am.Positions.ToTextOrTable("All Positions:")); })
        .SelectMany(_ => am.ComboTrades(1).ToArray()
        )
        .Select(trades => {
          var under = trades.Where(ct => ct.contract.LocalSymbol == "ESU0").Single();
          var coveredPut = AccountManager.CoveredPut(under.contract, trades).Single();
          return new { under, coveredPut };
        })
      .Subscribe(rollContext => {
        var a = MonoidsCore.ToFunc((ComboTrade ct) => new { ct.contract, ct.Change, ct.change, ct.pl, ct.position });
        var trade = rollContext.under;
        var cover = rollContext.coveredPut;
        //HandleMessage(trade.ToTextTable("Covered Put"), false);
        HandleMessage(new[] { a(trade),a(cover) }.ToTextOrTable("Roll Trade"), false);
        am.CurrentRollOver(trade.contract.LocalSymbol, false, 2, 2, 0)
        .ToArray()
        .Subscribe(roll => HandleMessage(roll.Select(r => new {
          r.roll, days = r.days.ToString("00"), bid = r.bid.ToString("0.00"), perc = r.perc.ToString("0.0"), dpw = r.dpw.ToInt(), r.ppw, r.amount, r.delta
        }).ToTextOrTable("Roll Overs")));
        //new { roll.roll, days = roll.days.ToString("00"), bid = roll.bid.ToString("0.00"), perc = roll.perc.ToString("0.0"), dpw = roll.dpw.ToInt(), roll.ppw, roll.amount, roll.delta }
      });
    }
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
