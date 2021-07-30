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
      am.CurrentOptions("VIX", 0, 0, 4, c => true)
      .Subscribe(se => {
        HandleMessage(se.Select(x => new { x.option }).ToMarkdownTable());
        am.CurrentOptions("ESH1", 0, 3, 4, c => true)
        .Subscribe(es => {
          HandleMessage(es.Select(x => new { x.option }).ToMarkdownTable());
          am.CurrentOptions("SPX", 0, 3, 4, c => true)
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

    public static void CurrentStraddles(AccountManager am) {
      var symbol = "SPY";
      var expDate = DateTime.Now.Date;//.Parse("7/25/2021");
      var gap = 5;
      AccountManager.MakeStraddles(symbol, 1, expDate, gap)
        //.OrderBy(c => c.options.Buffer(2).Max(o => o[0].Strike.Abs(o[1].Strike)))
        .ToArray()
              .Subscribe(straddles => {
                HandleMessage("\n" + straddles.Select(straddle => new { straddle.contract }).ToMarkdownTable());
              });
      return;
      am.CurrentStraddles("SPY", 0, 0, 2, 20)
        .Subscribe(cs => {
          HandleMessage(cs.Select(c => new { c.option }).ToMarkdownTable());
        });
    }

  }
}
