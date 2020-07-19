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
  static class PositionsTest {
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
        HandleMessage(comboPrices.Select(c=>new {c.contract,c.position,c.openPrice,c.closePrice }).ToTextOrTable(),false);
        HandleMessage2($"Matches: Done in {swCombo.ElapsedMilliseconds} ms =========================================");
      });
    }
  }
}
