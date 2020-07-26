using HedgeHog;
using IBApp;
using System;
using System.Linq;
using System.Reactive.Linq;
using static ConsoleApp.Program;
using MarkdownLog;

namespace ConsoleApp {
  static class CurrentOptionsTest {
    public static void CurrentOptions(AccountManager am) =>
      am.CurrentOptions("NQU0", 0, 3, 4, c => true)
      .Subscribe(se => {
        HandleMessage(se.Select(x => new { x.option }).ToMarkdownTable());
        am.CurrentOptions("ESU0", 0, 3, 4, c => true)
        .Subscribe(es => {
          HandleMessage(es.Select(x => new { x.option }).ToMarkdownTable());
        });
      });
    public static void ButterFly(AccountManager am) =>
      (from bf in am.MakeButterflies("ESU0", 3225)
       from oc in am.OpenTradeWithAction(o=>o.Transmit=false,bf.contract,1)
       select oc)
      .Subscribe();
      
  }
}
