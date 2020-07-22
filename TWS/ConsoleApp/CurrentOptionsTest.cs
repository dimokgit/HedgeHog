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
  }
}
