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
using static ConsoleApp.Program;

namespace ConsoleApp {
  static class Tests {
    public static IObservable<HedgeRatio> GetHedgePairInfo(string pos1, string pos2) =>
      from his in GetHedgeInfo(pos1, pos2).Buffer(2).Select(b => new { pos1 = b[0], pos2 = b[1] })
      select new HedgeRatio(his.pos1.c, his.pos2.c, (his.pos1.cap / his.pos2.cap) * (his.pos1.vlt / his.pos2.vlt));

    public static IObservable<HedgeInfo> GetHedgeInfo(string symbol, string hedge) =>
      (from s in new[] { symbol, hedge }.ToObservable()
       from hi in GetHedgeInfo(s)
       select hi
      );

    public static IObservable<HedgeInfo> GetHedgeInfo(string symbol) =>
      from c in symbol.ReqContractDetailsCached().Select(cd => cd.Contract)
      from hi in GetHedgeInfo(c)
      select hi;
    public static IObservable<HedgeInfo> GetHedgeInfo(Contract c) {
      var ibc = IBClientCore.IBClientCoreMaster;
      return (from p in c.ReqPriceSafe(10)
              let p2 = p
              //from tgo in ibc.TickGenericObservable
              from price in ibc.TryGetPrice(c)
              where price.OptionImpliedVolatility.ThrowIf(oiv => oiv == 0) > 0
              let info = new { mul = c.ComboMultiplier * (c.SecType == "STK" ? 1 : 1), p.avg, price.OptionImpliedVolatility }
              select new HedgeInfo(c, info.mul, info.avg, info.OptionImpliedVolatility.Round(3))
      )
      .OnEmpty(()=> HandleMessage(new { c, @is = "Empty"}))
      .FirstOrDefaultAsync();
      //.Subscribe(cd => HandleMessage(cd.ToJson(true)));
      //      IBClientCore.IBClientCoreMaster.TickGenericObservable.Subscribe(_ =>
      //        HandleMessage($"SPY Price:{IBClientCore.IBClientCoreMaster.TryGetPrice("SPY".ContractFactory()).Select(p => new { p.OptionImpliedVolatility }).FirstOrDefault()}"));
    }
    public static void MakeStockCombo(AccountManager am) {
      var portfolio = new[] { "AAPL", "MSFT", "AMZN" }.TakeLast(3).Select(s => s.ReqContractDetailsCached().Select(cd => cd.Contract)).Merge();
      (from contract in portfolio
       from price in contract.ReqPriceSafe()
       select new { contract, price }
       )
       .ToArray()
       .Subscribe(ps => {
         HandleMessage(ps.ToTextOrTable("Stocks"));
         var combo = AccountManager.MakeStockCombo(10000, ps.Select(l => (l.contract, l.price.avg)).ToList());
         combo.contract.ReqContractDetailsCached().Subscribe(_ => HandleMessage(_));
         combo.contract.ReqPriceSafe().Subscribe(p => HandleMessage(new { combo, p }.ToTextTable("Combo Price")));
         (from oc in am.OpenTradeWithAction(o => o.Transmit = false, combo.contract, combo.quantity)
          select oc
          ).Subscribe(oc => HandleMessage(oc));
       });
    }
    public static void MakeHedgeCombo(AccountManager am) {
      am.PositionsObservable.Do(positions => {
        HandleMessage(am.Positions.ToTextOrTable("All Positions:"));
      })
      .Where(_ => am.Positions.Count >= 2)
      .Take(1)
      .SelectMany(_ => am.MakeComboHedgeFromPositions(am.Positions))
      .Subscribe(c => {
        HandleMessage(new { c.contract, c.position, c.openPrice, c.closePrice, c.price.bid, c.price.ask }.ToTextTable("Hedge Combo"), false);
      });
    }
    public static void HedgeComboRatio(AccountManager am, string localSymbol1, string localSymbol2) {
      var parentContract = localSymbol1.ContractFactory();
      var hedgeContract = localSymbol2.ContractFactory();
      (from pc in parentContract.ReqContractDetailsCached().Select(cd => cd.Contract)
       from hc in hedgeContract.ReqContractDetailsCached().Select(cd => cd.Contract)
       from pPrice in pc.ReqPriceSafe()
       from hPrice in hc.ReqPriceSafe()
       let pMul = pc.ComboMultiplier
       let hMul = hc.ComboMultiplier
       select new { pPrice = pPrice.avg, hPrice = hPrice.avg, pMul, hMul, capRatio = pPrice.avg * pMul / hPrice.avg / hMul }
       ).Subscribe(x => HandleMessage(x));
    }
    public static void HedgeComboPrimary(AccountManager am, string localSymbol1, string localSymbol2) {
      var parentContract = localSymbol1.ContractFactory();
      var hedgeContract = localSymbol2.ContractFactory();
      var quantityParent = 10;
      var r = 1.8;// quantityParent / ((quantityParent / 2.26).Round(0) + 1);
      Func<(double p1, double p2)> hp = () => r.PositionsFromRatio();
      //while(new[] { (hp().p1 * 600).ToInt(), (hp().p2 * 600).ToInt() }.GCD() != 1) r += 0.01;
      (from hc in AccountManager.MakeHedgeComboSafe(quantityParent, parentContract, hedgeContract, hp().p1, hp().p2, false)
       from cd in hc.contract.ReqContractDetailsCached()
       let pcs = cd.Contract.HedgeComboPrimary((m1, m2) => throw new SoftException(new { m1, m2, error = "not found" } + ""))
       from pc in pcs
       from p in cd.Contract.ReqPriceSafe()

       select new { pc, p }
         )
         .Subscribe(x => {
           HandleMessage(new { parentContract, hedgeContract, primaryContract = x.pc, price = x.p });
         });
    }
    public static void HedgeCombo2(AccountManager am) {
      var parentContract = "MESM3".ContractFactory();
      var hedgeContract = "MBTK3".ContractFactory();
      var quantityParent = 1;
      var quantityHedge = -2;
      var isTest = true;
      (from hc in AccountManager.MakeHedgeComboSafe(1, parentContract, hedgeContract, quantityParent, quantityHedge, false)
       from cd in hc.contract.ReqContractDetailsCached()
       from p in cd.Contract.ReqPriceSafe().Select(ab => quantityParent > 0 ? ab.ask : ab.bid)
       from ot in am.OpenTradeWithAction(o => o.Transmit = !isTest, cd.Contract, hc.quantity, p)
       select ot
       )
       .Subscribe(c => {
         Program.HandleMessage(c.Select(t => new { t.holder, t.error }).ToTextOrTable("Test Order:"));
         Program.HandleMessage(am.OrderContractsInternal.Items.Select(t => new { t.order, t.contract }).ToTextOrTable("Test Order Holders:"));
       });
    }
    public static void HedgeCombo(AccountManager am, string parent, string hedge, double ratio, int quantityParent, int correlation) {
      var parentContract = parent.ContractFactory();
      var hedgeContract = hedge.ContractFactory();
      HedgeCombo(am, parentContract, hedgeContract, ratio, quantityParent, correlation);
    }
    public static void HedgeCombo(AccountManager am, Contract parentContract, Contract hedgeContract, double r, int quantityParent, int correlation) {
      Func<(double p1, double p2)> hp = () => r.PositionsFromRatio();
      //while(new[] { (hp().p1 * 600).ToInt(), (hp().p2 * 600).ToInt() }.GCD() != 1) r += 0.01;
      var isTest = true;
      (from hc in AccountManager.MakeHedgeComboSafe(quantityParent, parentContract, hedgeContract, hp().p1, hp().p2 * correlation, false)
       from cd in hc.contract.ReqContractDetailsCached()
       from p in cd.Contract.ReqPriceSafe().Select(ab => quantityParent > 0 ? ab.ask : ab.bid)
       from ot in am.OpenTradeWithAction(o => o.Transmit = !isTest, cd.Contract, hc.quantity, p)
       select ot
       )
       .Subscribe(c => {
         Program.HandleMessage(c.Select(t => new { t.holder, t.error }).ToTextOrTable("Test Order:"));
         Program.HandleMessage(am.OrderContractsInternal.Items.Select(t => new { t.order, t.contract }).ToTextOrTable("Test Order Holders:"));
       });
    }
    public static void HedgeCombo(AccountManager am) {

      {
        var parentContract = "SPY".ContractFactory();
        var hedgeContract = "VXX".ContractFactory();
        var quantityParent = 100;
        double r = 7;// quantityParent / ((quantityParent / 2.26).Round(0) + 1);
        Func<(double p1, double p2)> hp = () => r.PositionsFromRatio();
        //while(new[] { (hp().p1 * 600).ToInt(), (hp().p2 * 600).ToInt() }.GCD() != 1) r += 0.01;
        var isTest = true;
        (from hc in AccountManager.MakeHedgeComboSafe(quantityParent, parentContract, hedgeContract, hp().p1, -hp().p2, false)
         from cd in hc.contract.ReqContractDetailsCached()
         from p in cd.Contract.ReqPriceSafe().Select(ab => quantityParent > 0 ? ab.ask : ab.bid)
         from ot in am.OpenTradeWithAction(o => o.Transmit = !isTest, cd.Contract, hc.quantity, p)
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
     from p in ct.contract.ReqPriceSafe().Select(ab => ab.Price(true))
     from ots in am.OpenTradeWithAction(o => o.Transmit = false, ct.contract, -ct.position)
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
                    am.OpenTradeWithAction(o => o.Transmit = false, buy ? optionHedge.buy : optionHedge.sell, optionHedge.quantity)
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
      am.CurrentOptions(h1, double.NaN, (0, DateTime.MinValue), 10, c => true)
      .Subscribe(os => Program.HandleMessage(os.Select(o => new { o.option, o.marketPrice.bid, o.marketPrice.ask }).ToArray().ToTextOrTable("Options:")));


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
            am.OpenTradeWithAction(o => o.Transmit = false, ct.contract, -ct.position)
            .Subscribe(orderHolder => { Program.HandleMessage(orderHolder.ToTextOrTable()); });
            var combo = AccountManager.MakeHedgeCombo(maxLegQuantity, Contract.FromCache(h1).Single(), Contract.FromCache(h2).Single(), 1, 0.6).With(c => new { c.contract, c.quantity });
            var j2 = combo.contract.ToJson(true);
            var pos = -1;
            if(pos == -11)
              am.OpenTradeWithAction(o => o.Transmit = false, combo.contract, combo.quantity * pos)
              .Subscribe(orderHolder => { Program.HandleMessage(orderHolder.ToTextOrTable()); });
          }
        });

      });
      //return;
    }


    public static void CurrentOptionsTest(AccountManager am, string symbol) {
      am.CurrentOptions(symbol, double.NaN, (0, DateTime.MinValue), 2, c => true)
      .Subscribe(ss => Program.HandleMessage(ss.Select(a => a.option).Select(c => new { c.ShortString, c.DateWithShort, c.ShortWithDate2 }).ToTextOrTable("Options:")));
    }
  }

  internal struct HedgeInfo {
    public Contract c;
    public double mul;
    public double avg;
    public double vlt;
    public double cap => (mul * avg).Round(0);

    public HedgeInfo(Contract c, double mul, double avg, double vlt) {
      this.c = c;
      this.mul = mul;
      this.avg = avg;
      this.vlt = vlt;
    }

    public override bool Equals(object obj) => obj is HedgeInfo other && EqualityComparer<Contract>.Default.Equals(c, other.c) && mul == other.mul && avg == other.avg && vlt == other.vlt;

    public override int GetHashCode() {
      int hashCode = 2090083034;
      hashCode = hashCode * -1521134295 + EqualityComparer<Contract>.Default.GetHashCode(c);
      hashCode = hashCode * -1521134295 + mul.GetHashCode();
      hashCode = hashCode * -1521134295 + avg.GetHashCode();
      hashCode = hashCode * -1521134295 + vlt.GetHashCode();
      return hashCode;
    }

    public void Deconstruct(out Contract c, out double mul, out double avg, out double vlt) {
      c = this.c;
      mul = this.mul;
      avg = this.avg;
      vlt = this.vlt;
    }

    public static implicit operator (Contract c, double mul, double avg, double vlt)(HedgeInfo value) => (value.c, value.mul, value.avg, value.vlt);
    public static implicit operator HedgeInfo((Contract c, double mul, double avg, double vlt, double cap) value) => new HedgeInfo(value.c, value.mul, value.avg, value.vlt);
    public override string ToString() => new { c = c.ToString(), mul, avg, vlt, cap }.ToString();
  }

  internal struct HedgeRatio {
    public Contract pos1;
    public Contract pos2;
    public double ratio;

    public HedgeRatio(Contract pos1, Contract pos2, double ratio) {
      this.pos1 = pos1;
      this.pos2 = pos2;
      this.ratio = ratio;
    }

    public override bool Equals(object obj) => obj is HedgeRatio other && EqualityComparer<Contract>.Default.Equals(pos1, other.pos1) && EqualityComparer<Contract>.Default.Equals(pos2, other.pos2) && ratio == other.ratio;

    public override int GetHashCode() {
      int hashCode = -1111949003;
      hashCode = hashCode * -1521134295 + EqualityComparer<Contract>.Default.GetHashCode(pos1);
      hashCode = hashCode * -1521134295 + EqualityComparer<Contract>.Default.GetHashCode(pos2);
      hashCode = hashCode * -1521134295 + ratio.GetHashCode();
      return hashCode;
    }

    public void Deconstruct(out Contract pos1, out Contract pos2, out double ratio) {
      pos1 = this.pos1;
      pos2 = this.pos2;
      ratio = this.ratio;
    }

    public static implicit operator (Contract pos1, Contract pos2, double ratio)(HedgeRatio value) => (value.pos1, value.pos2, value.ratio);
    public static implicit operator HedgeRatio((Contract pos1, Contract pos2, double ratio) value) => new HedgeRatio(value.pos1, value.pos2, value.ratio);
    public override string ToString() => new {
      pos1 = pos1?.ToString() ?? "null", pos2 = pos2?.ToString() ?? "null", ratio
    }.ToString();
  }
}
