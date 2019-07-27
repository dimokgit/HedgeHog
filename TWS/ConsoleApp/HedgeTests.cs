using HedgeHog;
using IBApp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp {
  static class Tests {
    public static void HedgeCombo(IBClientCore ibClient, AccountManager am) {
      var h1 = "ESU9";
      var h2 = "NQU9";
      var maxLegQuantity = 10;
      //am.CurrentOptions(h1, double.NaN, 0, 10,c=>true)
      //.Subscribe(os => HandleMessage(os.Select(o => new { o.option }).ToArray().ToTextOrTable("Options:")));

      am.CurrentHedges(h1, h2)
      .Subscribe(hh => {
        Program.HandleMessage(hh.Select(h => new { h.contract, h.ratio, amount = h.price * h.ratio, h.context }).ToTextOrTable("Hedge"));
        am.CurrentHedges(h1, h2, "", c => c.ShortWithDate2)
        .Subscribe(hh2 => {
          Program.HandleMessage(hh2.Select(h => new { h.contract, h.ratio, amount = h.price * h.ratio, h.context }).ToTextOrTable("Hedge 2"));
          var combo = AccountManager.MakeHedgeCombo(maxLegQuantity, hh2[0].contract, hh2[1].contract, hh2[0].ratio, hh2[1].ratio).With(c => new { combo = c, context = hh2.ToArray(t => t.context).MashDiffs() });
          var a = new[] { new { combo.combo.contract.ShortString, combo.combo.contract.DateWithShort, combo.combo.contract.ShortWithDate, combo.combo.contract, combo.context } };
          Program.HandleMessage($"{a.ToTextOrTable("Hedge Combo:")}");
          (from p in ibClient.ReqPriceSafe(combo.combo.contract) select new { combo.combo.contract, p.bid, p.ask }).Subscribe(Program.HandleMessage);
          am.ComboTrades(5).ToArray().Subscribe(posHedges
          => Program.HandleMessage(posHedges.Select(h => new { h.contract.ShortString, h.open, h.close, h.pl, h.closePrice }).ToTextOrTable("Position Hadge:")));
          var pos = -1;
          if(pos == 1) {
            //am.OpenTrade(combo.combo.contract, combo.combo.quantity * pos)
            //.Subscribe(orderHolder => {
            //  HandleMessage(orderHolder.ToTextOrTable());
            //});
            am.OpenTrade(o => o.Transmit = false, combo.combo.contract, combo.combo.quantity * -pos)
        .Subscribe(orderHolder => {
          Program.HandleMessage(orderHolder.ToTextOrTable());
        });
          }
        });

      });

    }

    public static void CurrentOptionsTest(AccountManager am, string h1) {
      am.CurrentOptions(h1, double.NaN, 0, 2, c => true)
      .Subscribe(ss => Program.HandleMessage(ss.Select(a => a.option).Select(c => new { c.ShortString, c.DateWithShort, c.ShortWithDate2 }).ToTextOrTable("Options:")));
      am.CurrentStraddles(h1, double.NaN, 0, 2, 1)
      .Subscribe(ss => Program.HandleMessage(ss.Select(a => a.combo.contract).Select(c => new { c.ShortString, c.DateWithShort, c.ShortWithDate2 }).ToTextOrTable("Straddles:")));
    }
  }
}
