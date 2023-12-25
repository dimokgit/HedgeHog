using HedgeHog;
using IBApp;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using static ConsoleApp.Program;

namespace ConsoleApp {
  static partial class OrdersTest {
    public static void HedgeOrder(AccountManager am) {
      am.OrderContractsInternal.Connect()
      .Subscribe(_ => {
        (from oc in am.OrderContractsInternal.Items.ToObservable()
         from p in oc.contract.ReqPriceSafe().DefaultIfEmpty()
         select new { oc, p,oc.contract.ComboMultiplier }
         )
         .Subscribe(x => HandleMessage(x.ToTextTable("Hedge Order")));
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
    public static void OpenTrailLimit(this AccountManager am, IObservable<HedgeCombo> c, IObservable<MarketPrice> price,double trailingAmount,int quantity) {
      /// https://www.interactivebrokers.com/en/trading/orders/trailing-stop-limit.php
      (from combo in c
       from p in price
       from ots in am.OpenTradeWithAction(o => {
         o.Transmit = false;
         o.OrderType = "TRAIL LIMIT";
         o.TrailStopPrice = p.avg - trailingAmount;
         o.LmtPriceOffset = -trailingAmount;
         o.AuxPrice = trailingAmount;
       }, combo.contract, quantity, 0, 0, false, DateTime.MinValue)
       from ot in ots
       select ot
       )
      .Subscribe(x => HandleMessage(new { order = x.value, x.error }.ToTextTable()));
    }
  }
}
