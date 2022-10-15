using HedgeHog;
using IBApp;
using System;
using System.Linq;
using System.Reactive.Linq;
using static ConsoleApp.Program;
using MarkdownLog;
using System.Collections.Generic;
using IBApi;

namespace ConsoleApp {
  static class CurrentOptionsTest {
    public static void CurrentOptions(AccountManager am) =>
      am.CurrentOptions("VIX", 0, (0, DateTime.MinValue), 4, c => true)
      .Subscribe(se => {
        HandleMessage(se.Select(x => new { x.option }).ToMarkdownTable());
        am.CurrentOptions("ESH1", 0, (3, DateTime.MinValue), 4, c => true)
        .Subscribe(es => {
          HandleMessage(es.Select(x => new { x.option }).ToMarkdownTable());
          am.CurrentOptions("SPX", 0, (3, DateTime.MinValue), 4, c => true)
          .Subscribe(spx => {
            HandleMessage(spx.Select(x => new { x.option }).ToMarkdownTable());
          });
        });
      });
    public static void ButterFly(AccountManager am) =>
      (from bf in am.MakeButterflies("ESU0", 3225)
       from oc in am.OpenTradeWithAction(o => o.Transmit = false, bf.contract, 1)
       select oc)
      .Subscribe();

    public static void CurrentStraddles(AccountManager am, string symbol) =>
      am.CurrentStraddles(symbol, double.NaN, (1, DateTime.MinValue), 1, 1)
      .Subscribe(ss => Program.HandleMessage(ss.Select(a => a.combo.contract).Select(c => new { c.ShortString, c.DateWithShort, c.ShortWithDate2 }).ToTextOrTable("Straddles:")));

    public static void CurrentStraddles(AccountManager am) {
      var symbol = "ESU1";
      var expDate = DateTime.Now.Date;//.Parse("7/25/2021");
      var gap = 5;
      AccountManager.MakeStraddles(symbol, 1, expDate, gap)
        //.OrderBy(c => c.options.Buffer(2).Max(o => o[0].Strike.Abs(o[1].Strike)))
        .ToArray()
              .Subscribe(straddles => {
                HandleMessage("\n" + straddles.Select(straddle => new { straddle.contract }).ToMarkdownTable());
              });
      return;
      am.CurrentStraddles("SPY", 0, (0, DateTime.MinValue), 2, 20)
        .Subscribe(cs => {
          HandleMessage(cs.Select(c => new { c.option }).ToMarkdownTable());
        });
    }
    public static void CustomStraddle(AccountManager am, IList<string> options) {
      var bookStraddle = (from bp in options.ToObservable()
                          from c in bp.ReqContractDetailsCached()
                          select c.Contract
                          ).ToArray()
                          .Where(cp => cp.Length == 2)
                          .SelectMany(cp => am.StraddleFromContracts(cp))
              .Subscribe(straddles => {
                HandleMessage("\n" + straddles.Select(straddle => new { straddle.combo.contract, straddle.marketPrice, straddle.underPrice }).ToMarkdownTable());
                (from oc in am.OpenTradeWithAction(o => o.Transmit = false, straddles[0].combo.contract, 1, straddles[0].marketPrice.ask)
                 select oc
                 ).Subscribe();
              });
      ;

    }

  }
}
