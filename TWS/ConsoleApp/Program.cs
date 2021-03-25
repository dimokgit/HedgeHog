using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IBApi;
using IBApp;
using HedgeHog;
using HedgeHog.Shared;
using HedgeHog.Bars;
using HedgeHog.Core;
using System.Reactive.Disposables;
using System.Reactive.Concurrency;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reactive;
using MarkdownLog;
using HedgeHog.Alice.Store;
using HedgeHog.DateTimeZone;
using System.Runtime.ExceptionServices;
using System.Windows;

namespace ConsoleApp {
  class Program {
    [DllImport("kernel32.dll", ExactSpelling = true)]

    private static extern IntPtr GetConsoleWindow();
    private static IntPtr ThisConsole = GetConsoleWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]

    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int HIDE = 0;
    private const int MAXIMIZE = 3;
    private const int MINIMIZE = 6;
    private const int RESTORE = 9;
    static void Main(string[] args) {
      HedgeHog.ConfigGlobal.DoRunTest = false;
      AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
      #region Init
      ShowWindow(ThisConsole, MAXIMIZE);
      TradesManagerStatic.dbOffers = GlobalStorage.LoadOffers();
      Contract.HedgePairs = GlobalStorage.LoadHadgePairs().Select(h => (h.hedge1, h.hedge2, h.prime)).ToArray();
      int _nextValidId = 0;

      TradesManagerStatic.AccountCurrency = "USD";
      var ibClient = IBClientCore.Create(o => HandleMessage(o + ""));
      ibClient.CommissionByTrade = (t) => 2;
      ibClient.NextValidId += id => _nextValidId = id;
      ibClient.CurrentTime += time => HandleMessage("Current Time: " + ibClient.ServerTime + "\n");

      var coreFx = ibClient as ICoreFX;
      //coreFx.LoginError += HandleError;
      coreFx.SubscribeToPropertyChanged(ibc => ibc.SessionStatus, ibc => HandleMessage(new { ibc.SessionStatus } + ""));
      //ibClient.PriceChanged += OnPriceChanged;

      var fw = new IBWraper(coreFx, _ => 0);
      var usdJpi2 = ContractSamples.FxContract("usd/jpy");
      var gold = ContractSamples.Commodity("XAUUSD");
      var vx = ContractSamples.ContractFactory("VXH8");
      var spy = ContractSamples.ContractFactory("SPY");
      var svxy = ContractSamples.ContractFactory("SVXY");
      //var opt = ContractSamples.Option("SPX","20180305",2695,true,"SPXW");
      var opt = ContractSamples.Option("SPXW  180305C02680000");
      DataManager.DoShowRequestErrorDone = true;
      const int twsPort = 7496;
      const int clientId = 11;
      ReactiveUI.MessageBus.Current.Listen<LogMessage>().Subscribe(lm => HandleMessage(lm.ToJson()));
      bool Connect() => ibClient.LogOn("127.0.0.1", twsPort + "", clientId + "", false);
      #endregion
      StartTests();
      if(Connect()) {
        //ibClient.SetOfferSubscription(contract);
        //else {
        //  var sp500 = HedgeHog.Alice.Store.GlobalStorage.UseForexContext(c => c.SP500.Where(sp => sp.LoadRates).ToArray());
        //  var dateStart = DateTime.UtcNow.Date.ToLocalTime().AddMonths(-1).AddDays(-2);
        //  foreach(var sp in sp500.Select(b => b.Symbol)) {
        //    HedgeHog.Alice.Store.PriceHistory.AddTicks(fw, 1, sp, dateStart, o => HandleMessage(o + ""));
        //  }
        //}
      } else HandleMessage("********** Didn't connect ***************");

      void StartTests() {
        ibClient.ManagedAccountsObservable.Subscribe(s => {
          var am = fw.AccountManager;
          TestCombosTrades(10).Subscribe(); return;
          Tests.HedgeComboPrimary(am, "MESH1", "CLJ1"); return;
          Tests.HedgeCombo(am); return;
          LoadMultiple(DateTime.Now.AddMonths(-14), 5, "SPY", "QQQ"); return;
          CurrentOptionsTest.CurrentOptions(am); return;
          "VIXW  210302C00027000".ReqContractDetailsCached().SelectMany(cd => cd.Contract.ReqPriceSafe()).Subscribe(HandleMessage); return;
          Tests.MakeHedgeCombo(am); return;
          //new Contract { Symbol = "VIX", SecType = "FUT+CONTFUT", Exchange = "CFE" }.ReqContractDetailsCached()
          new Contract { Symbol = "ZN", SecType = "FUT+CONTFUT", Exchange = "ECBOT" }.ReqContractDetailsCached()
          .Subscribe(cd => {
            HandleMessage(cd.ToJson(true));
            cd.Contract.ReqPriceSafe().Subscribe(HandleMessage);
          });
          return;
          {
            var secs = new[] { "SPY", "VXX", "ESZ0", "NQZ0", "RTYZ0" }.Take(1).ToArray();
            Console.WriteLine("Loadint " + secs.Flatter(","));
            LoadMultiple(DateTime.Now.AddMonths(-1), 3, secs);
            return;
          }
          PositionsTest.RollAutoOpen(am); return;
          Tests.MakeStockCombo(am); return;
          OrdersTest.HedgeOrder(am); return;
          EdgeTests.OpenEdgeCallPut(am).Subscribe(_ => PositionsTest.ComboTrades(am));
          return;
          var ess = new[] { "VXM0", "NQZ9", "ESM0", "RTYZ9", "IWM", "SPY", "QQQ" };
          return;
          am.PositionsObservable.Subscribe(_ => HandleMessage(am.Positions.ToTextOrTable("All Positions:")));
          return;
          new Contract { Symbol = "VIX", SecType = "FUT+CONTFUT", Exchange = "CFE" }.ReqContractDetailsCached()
          .Subscribe(cd => HandleMessage(cd.ToJson(true)));
          return;
          void LoadMultiple(DateTime dateStart, int period, params string[] secs) {// Load bars
            /** Load History
            var c = new Contract() {
              Symbol = "ES",
              SecType = "CONTFUT",
              Exchange = "GLOBEX"
            };
            LoadHistory(ibClient, new[] { c });
            */
            bool repare = false;
            Action<object> callback = o => HandleMessage(o + "");
            secs.ToObservable()
            .Do(sec =>
            sec.ContractFactory().ReqContractDetailsCached()
            .SubscribeOn(TaskPoolScheduler.Default)
            .ObserveOn(TaskPoolScheduler.Default)
            .Subscribe(_ => {
              if(repare) {
                var bars = new List<Rate>();
                Action<RateLoadingCallbackArgs<Rate>> showProgress = (rlcArgs) => {
                  //PriceHistory.SaveTickCallBack(period, sec, callback, rlcArgs);
                  HandleMessage(rlcArgs.Message);
                  rlcArgs.IsProcessed = true;
                };
                //fw.GetBarsBase(es, period, 0, DateTime.Now.AddMonths(-5).SetKind(), DateTime.Now.SetKind(), bars, null, showProgress);
                //fw.GetBarsBase(sec, period, 0, DateTime.Parse("11/29/2019").SetLocal(), DateTime.Parse("11/29/2019 23:59").SetLocal()
                fw.GetBarsBase(sec, period, 8000, TradesManagerStatic.FX_DATE_NOW, DateTime.Now.SetLocal(), true, false
                    , bars, null, showProgress);
                HandleMessage($"***** Done GetBars {bars.Count}*****");
              } else {
                PriceHistory.AddTicks(fw, period, sec, dateStart, callback, false);
                HandleMessage($"***** Done AddTick *****");
              }
            })).Subscribe();
            HandleMessage($"ManagedAccountsObservable thread:{Thread.CurrentThread.Name.IfTrue(th => th.IsNullOrEmpty(), th => Thread.CurrentThread.ManagedThreadId + "")}");
            return;
          }
          {
            "IWM".ContractFactory().ReqContractDetailsCached()
            .Subscribe(cd => LoadHistory(ibClient, new[] { cd.Contract }));
            return;
          }
          //Tests.HedgeCombo(am);
          //return;
          {// double same order
            (from c in "spy".ContractFactory().ReqContractDetailsCached().Select(cd => cd.Contract)
             from ot in am.OpenTrade(c, 1)
             from ot2 in am.OpenTrade(c, 1)
             select ot.Concat(ot2)
             )
            .Subscribe(c => {
              HandleMessage(c.Select(t => new { t.holder, t.error }).ToTextOrTable("Test Order:"));
              HandleMessage(am.OrderContractsInternal.Items.Select(t => new { t.order, t.contract }).ToTextOrTable("Test Order Holders:"));
            });
            return;
          }
          var isTest = true;
          am.OpenOrderObservable.Subscribe(oom => HandleMessage(oom.ToTextTable("Open Order")));
          {
            (from c in "SPY".ContractFactory().ReqContractDetailsCached().Select(cd => cd.Contract)
             from ots in am.OpenTradeWithAction(o => o.SetType(c, DateTime.Now, IBApi.Order.OrderTypes.MIDPRICE).Transmit = !isTest, c, 1)
               //from ots in am.OpenTrade(c, 1)
             from ot in ots
             select new { ot.holder, ot.error }
             )
             .ToArray()
             .Subscribe(ot => HandleMessage(ot.ToTextOrTable("Open Trade")));
            return;
          }

          return;
          {
            "SPY".ContractFactory().ReqContractDetailsCached()
            //.ObserveOn(TaskPoolScheduler.Default)
            .Select(cd => cd.Contract)
            .Subscribe(contract => HandleMessage($"***** {new { contract }} *****"));
            return;
          }

          {
            //(from o in am.OpenOrderObservable
            // from combo in AccountManager.MakeHedgeComboSafe(10, "VXX".ContractFactory(), "UVXY".ContractFactory(), 10, 7)
            // from Price in o.Contract.ReqPriceSafe()
            // let s0 = combo.contract.DateWithShort
            // let s1 = combo.contract.ShortString
            // select new { o.Contract, Price, j1 = combo.contract.ToJson(true), j2 = o.Contract.ToJson(true) }
            // ).Subscribe(x => HandleMessage($"Open Order Contract price:" + x));
            //return;
            (
            //from combo in AccountManager.MakeHedgeComboSafe(1, "VXX".ContractFactory() ,"UVXY".ContractFactory(), 40, 23)
            from combo in AccountManager.MakeHedgeComboSafe(1, "SPY".ContractFactory(), "QQQ".ContractFactory(), 40, 23, false)
            from p in combo.contract.ReqPriceSafe(5)
            select new { combo.contract, combo.quantity, p }
             )
            .Subscribe(combo => HandleMessage("HedgeCombo:" + combo + "\n" + combo.contract.ToJson(true)));
            return;
          }


          //am.OrderContractsInternal.Subscribe(o => { });
          {
            var symbol = "ESH9";//"NQU9";// "VXG9";//"RTYM9";
            ibClient.ReqContractDetailsCached(symbol)
            .Subscribe(cd => PriceHistory.AddTicks(fw, 3, symbol, DateTime.Now.AddMonths(-(12 * 0 + 4)), o => HandleMessage(o + " : Tread " + Thread.CurrentThread.Name)));
            return;
          }
          Tests.CurrentOptionsTest(am, "ESU9");
          Tests.CurrentOptionsTest(am, "esu9"); return;
          {
            (from p in am.PositionsObservable
             from cd in ibClient.ReqContractDetailsCached(p.Contract)
             select new { cd.Contract, p.Position }
               )
               .Take(2).ToArray().Subscribe(hh => {
                 HandleMessage(hh.Select(x => new { x.Contract.Instrument, x.Position }).ToTextOrTable("Positions"));
                 var combo = AccountManager.MakeHedgeCombo(1, hh[0].Contract, hh[1].Contract, hh[0].Position, -hh[1].Position);
                 HandleMessage("HedgeCombo:" + new { combo.contract, combo.quantity });
               });
            return;
          }
          {
            (from cs in ibClient.ReqOptionChainOldCache("ESU9", default, 3000)
             from c in cs
             where c.IsCall
             select c
             ).Subscribe(c => am.OpenTradeWithConditions(c.LocalSymbol, 1, 5, 2640, false));
            return;
          }
          {
            Observable.Interval(1.FromSeconds())
             .TakeWhile(_ => am.Positions.Count < 3).ToArray()
             .SelectMany(contracts => {
               return am.TradesBreakEvens("");
               //HandleMessage(contracts.ToTextOrTable("Positions"));
               //return true;
             }).Subscribe(bes => {
               HandleMessage(bes.Select(be => new { be.level, be.isCall }).ToTextOrTable("Trades Break Evens"));
             });
            return;
          }
          {
            (from p in am.PositionsObservable
             from cd in ibClient.ReqContractDetailsCached(p.Contract)
             select cd.Contract
             )
             .Take(2).ToArray().Subscribe(contracts => {
               HandleMessage(contracts.ToTextOrTable("Positions"));
               var contract1 = contracts.Where(c => c.IsOption).Single();
               var contract2 = contracts.Where(c => !c.IsOption).Single();
               am.OpenCoveredOption(contract1, "MKT", 14, 0, contract2, 7, DateTime.MaxValue, DateTime.Now.AddDays(1), OrderConditionParam.PriceCondition(contract2, 2860, false));
             });
            return;
          }
          {
            (
            from cd in ibClient.ReqContractDetailsCached(new Contract {
              LocalSymbol = "EWH9 P2735",
              SecType = "FOP"
              //Strike = 2735, LastTradeDateOrContractMonth = "20190313", Right ="P",Symbol="ES",SecType="FOP"
            })
            from p in cd.Contract.SideEffect(c => HandleMessage(new { c })).ReqPriceSafe()
            select p
            ).Subscribe(p => HandleMessage(new { p }));

          }
          {
            (from options in am.CurrentOptions("ESH9", 0, 1, 3, c => true)
             select options
             )
             .Subscribe(options => {
               HandleMessage(options.Select(o => new { o.option }).OrderBy(_ => _.option.Strike).ToTextOrTable("CurrentOptions:"));
               //HandleMessage("One more time");
               //am.CurrentOptions("VXG9", 0, 2, 3, c => true).Subscribe(_ => HandleMessage(_.Select(o => new { o.instrument }).OrderBy(__ => __.instrument).ToMarkdownTable()));
             });
          }
          am.PositionsEndObservable.Subscribe(positions => HandleMessage(am.Positions.ToTextOrTable("All Positions:")));
          {
            Task.Delay(3000).ContinueWith(_ => {
              (from trade in am.Positions.Select(p => p.contract).Take(1).ToObservable()
               from rolls in am.CurrentRollOver(trade.LocalSymbol, false, 2, 2, 0).OrderByDescending(r => r.dpw).ToArray()
               select new { rolls, trade.LocalSymbol }
              )
              .Do(rolls => HandleMessage($"Rolls for {rolls.LocalSymbol}:\n" + rolls.rolls.Select(roll => new { roll.roll, roll.days, roll.bid }).ToMarkdownTable()))
              .SelectMany(a => a.rolls.OrderBy(o => o.roll.Expiration).Take(1).ToArray())
              .Take(1)
              .Subscribe(r => {
                HandleMessage("Trade this:\n" + new { r.roll, r.days, r.bid }.Yield().ToMarkdownTable());
                (from kv in IBClientCore.ReqContractDetails.ToObservable()
                 from cd in kv.Value
                 select new { kv.Key, cd.Contract, cd.Contract.Instrument, cd.Contract.LastTradeDateOrContractMonth })
                 .Take(0)
                 .ToArray()
                 .Subscribe(a => {
                   var o = a.OrderBy(x => x.Key).ThenBy(x => x.Contract.Instrument).ToArray();
                   HandleMessage(o.ToTextOrTable("ReqContractDetails:"));
                 });
                //am.OpenTrade(roll.trade.contract, -roll.trade.position, 0, 0, false, DateTime.MaxValue);
                //am.OpenTrade(roll.roll, roll.trade.position, 0, 0, false, DateTime.MaxValue);
                //am.OpenRollTrade("EW1F9 P2480", "E1AF9 P2455");
              });
            });
          }
          am.OrderStatusObservable.Throttle(1.FromSeconds()).Subscribe(_ => HandleMessage("OrderContractsInternal2:\n" + am.OrderContractsInternal.Items.ToMarkdownTable()));
          {
            //am.CancelOrder(50002201).Subscribe(m => HandleMessage("CancelOrder(50002201)" + m));
            //return;
            var ot = MonoidsCore.ToFunc((Contract c) => am.OpenTrade(c, 1, 2610, 0, false, default, DateTime.Now.AddDays(2)));
            (from c in ibClient.ReqContractDetailsCached("ESH9").Select(cd => cd.Contract)
             from posd in new[] { ot(c), ot(c) }
             from pos in posd
             select (pos, c)
             )
             .Do(_ => ot(_.c).Subscribe())
             .Select(t => t.pos)
            .Subscribe();
            //fw.OpenTrade("ESH9", true, 1, 5, 0, 2635, "OPT");
          }
          {
            void TestCurrentRollOvers(int num, Action after = null) {
              HandleMessage($"TestCurrentRollOvers:  start {num}");
              am.CurrentRollOvers("E3CF9 C2595", false, 2, 3)
              .Subscribe(_ => {
                HandleMessage(_.Select(c => new { c = c.ToString() }).ToMarkdownTable());
                HandleMessage($"TestCurrentRollOvers:  done {num}");
                after?.Invoke();
              });
            }
            TestCurrentRollOvers(1, () => TestCurrentRollOvers(2, () => TestCurrentRollOvers(3, () => TestCurrentRollOvers(4))));
            TestCurrentRollOvers(5);
          }

          return;
          {
            ibClient.ReqOptionChainOldAsync("VXG9", DateTime.MinValue, 19, true)
              .Take(1)
              .Subscribe(c => HandleMessage(c.ToJson(true)));
            ibClient.ReqOptionChainOldAsync("ESH9", DateTime.MinValue, 2600, true)
              .Take(1)
              .Subscribe(c => HandleMessage(c.ToJson(true)));
            ibClient.ReqOptionChainOldAsync("VIX", DateTime.MinValue, 19, true)
              .Take(1)
              .Subscribe(c => HandleMessage(c.ToJson(true)));
          }
          {
            var ochc0 = new Contract { Symbol = "ES", SecType = "FOP", Strike = 2590, Currency = "USD" };
            var ochc = new Contract { Symbol = "VIX", SecType = "OPT", Strike = 19, Currency = "USD" };
            ibClient.ReqContractDetailsCached(ochc)
              .Take(1)
              .Subscribe(c => HandleMessage(c.ToJson(true)));
          }
          {
            TestCurrentOptions(0);
            void TestCurrentOptions(int expirationDaysToSkip, bool doMore = true) {
              (from under in ibClient.ReqContractDetailsCached("ESH9")
               from price in under.Contract.ReqPriceSafe()
               from options in am.CurrentOptions(under.Contract.LocalSymbol, price.ask.Avg(price.bid), expirationDaysToSkip, 10, o => o.IsCall)
               from option in options
               select new { price.bid, option.option }
              )
              .ToArray()
              .Subscribe(_ => {
                HandleMessage("\n" + _.ToMarkdownTable());
                if(doMore)
                  TestCurrentOptions(0, false);
                //if(false)
                //am.OpenTrade(_.option.option, "", 1, 0, 0, false, DateTime.MaxValue, OrderConditionParam.PriceFactory(_.under, 3000, true, false));
              });
            }
            return;
          }
          {
            (from i in Observable.Interval(5.FromSeconds())
             from p in "ESH9".ContractFactory().ReqPriceSafe()
             select p
             ).Take(10)
             .Subscribe(p => HandleMessage(p));
          }
          return;
          {
            void TestAllStrikesAndExpirations(int num, Action afrer = null) {
              HandleMessage($"TestAllStrikesAndExpirations: Start {num}");
              ibClient.ReqStrikesAndExpirations("ESH9")
              .Subscribe(_ => {
                HandleMessage(new { strikes = _.strikes.Length, exps = _.expirations.Length });
                HandleMessage($"TestAllStrikesAndExpirations: End {num}");
                afrer?.Invoke();
              });
            }
            TestAllStrikesAndExpirations(1);
            TestAllStrikesAndExpirations(2, () => TestAllStrikesAndExpirations(3));
          }
          {
            Contract ESVXXContract(string symbol, int conId1, int conId2, int ratio1, int ratio2) {
              Contract contract = new Contract();
              contract.Symbol = symbol;
              contract.SecType = "BAG";
              contract.Currency = "USD";
              contract.Exchange = "SMART";

              ComboLeg leg1 = new ComboLeg();
              leg1.ConId = conId1;
              leg1.Ratio = ratio1;
              leg1.Action = "BUY";
              leg1.Exchange = "SMART";

              ComboLeg leg2 = new ComboLeg();
              leg2.ConId = conId2;
              leg2.Ratio = ratio2;
              leg2.Action = "BUY";
              leg2.Exchange = "SMART";

              contract.ComboLegs = new List<ComboLeg>();
              contract.ComboLegs.Add(leg1);
              contract.ComboLegs.Add(leg2);

              return contract;
            }
            (from esConId in ibClient.ReqContractDetailsCached("SPY").Select(cd => cd.Contract.ConId)
             from vxConId in ibClient.ReqContractDetailsCached("VXX").Select(cd => cd.Contract.ConId)
             select (esConId, vxConId)
             ).Subscribe(t => {
               am.OpenTrade(ESVXXContract("SPY,VXX", t.esConId, t.vxConId, 1, 3), 1, 0, 0, false, default, default, DateTime.Now.AddMonths(1).TimeCondition());
               am.OpenTrade(ESVXXContract("SPY,VXX", t.esConId, t.vxConId, 1, 2), 1, 0, 0, false, default, default, DateTime.Now.AddMonths(1).TimeCondition());
             });
          }
          var cdSPY = ibClient.ReqContractDetailsCached("SPY").ToEnumerable().ToArray();
          var cdSPY2 = ibClient.ReqContractDetailsCached("SPY").ToEnumerable().ToArray();
          Task.Delay(2000).ContinueWith(_ => {
            ibClient.ReqCurrentOptionsAsync("ESM8", 2670, new[] { true, false }, 0, 1, 10, c => true)
            .ToArray()
            .ToEnumerable()
            .ForEach(cds => {
              cds.Take(50).ForEach(cd => HandleMessage2(cd));
              HandleMessage("ReqCurrentOptionsAsync =============================");
            });
            ibClient.ReqCurrentOptionsAsync("ESM8", 2670, new[] { true, false }, 0, 1, 10, c => true)
            .ToArray()
            .ToEnumerable()
            .ForEach(cds => {
              cds.Take(50).ForEach(cd => HandleMessage2(cd));
              HandleMessage("ReqCurrentOptionsAsync =============================");
            });
            //TestMakeBullPut("ESM8", false);
          }); return;
          TestCurrentStraddles(1, 1);
          TestCurrentStraddles(1, 1); return;
          var cdsVXX = ibClient.ReqContractDetailsCached("VXX   180329C00051500".ContractFactory()).ToEnumerable().Count(0, new { }).ToArray();
          var symbols = new[] { "SPX", "VXX", "SPY" };
          var timeOut = Observable.Return(0).Delay(TimeSpan.FromSeconds(100)).Timeout(TimeSpan.FromSeconds(15 * 1000)).Subscribe();
          Stopwatch sw = Stopwatch.StartNew();
          //.ForEach(trade => HandleMessage(new { trade.Pair}));

          ProcessSymbols(1).Concat(TestCombosTrades(1)).ToEnumerable()
          .ForEach(_ => {
            timeOut.Dispose();
            LoadHistory(ibClient, new[] { "spx".ContractFactory() });
          });

          //ShowTrades(fw);
          //TestStraddleds();return;
          //Contract.Contracts.OrderBy(c => c + "").ForEach(cached => HandleMessage(new { cached }));
          //HandleMessage(nameof(ProcessSymbol) + " done =========================================================================");

          ibClient.ReqContractDetailsCached(321454967)
          .Subscribe(cd => {
            HandleMessage(cd.Contract.ToJson(true));
            ibClient.ReqContractDetailsCached(321454967)
            .Subscribe(cd2 => {
              HandleMessage(cd2.Contract.ToJson(true));
            });
          });
          { // Change order condition
            am.OpenOrderObservable.Subscribe(oom => {
              var order = am.OrderContractsInternal.Items.First();
              order.order.Conditions.Cast<PriceCondition>().ForEach(pc => pc.Price += 5);
              am.CancelOrder(order.order.OrderId).Subscribe(m => HandleMessage(m));
              //am.PlaceOrder(order.order, order.contract).Subscribe(po => HandleMessage(po));
              //HandleMessage(orders.Select(oc => new { oc.order, oc.contract }).ToTextOrTable("Ordes:"));
            });
            return;
          }

          #region Local Tests
          void TestMakeBullPut(string symbol, bool placeOrder) {
            HandleMessage2("MakeBullPut Start");
            am.CurrentBullPuts(symbol, double.NaN, 1, 5, 0)
            .ToEnumerable()
            .Concat()
            .Count(5, $"{nameof(TestMakeBullPut)}")
            .ForEach(comboPrice => {
              ibClient.ReqContractDetailsCached(comboPrice.combo.contract)
              .Subscribe(cd => {
              });
              comboPrice.combo.contract.Legs().Buffer(2).ForEach(b => Passager.ThrowIf(() => b[0].c.ComboStrike() - b[1].c.ComboStrike() != 5));
              HandleMessage2(new { comboPrice.combo.contract });
              comboPrice.combo.contract.ReqPriceSafe()
              .ToEnumerable()
              .ForEach(price => {
                HandleMessage($"Observed {comboPrice.combo.contract} price:{price}");
                if(placeOrder) {
                  HandleMessage2($"Placing SELL order for{comboPrice.combo.contract}");
                  am.OpenTrade(comboPrice.combo.contract, -1, price.ask.Avg(price.bid) * 0.55, 0, false, DateTime.MinValue);
                }
              });
              HandleMessage2($"MakeBullPut Done ==================");
            });

          }
          void TestMakeComboAll(bool placeOrder) {
            HandleMessage2("ComboTrade Start");
            AccountManager.MakeComboAll(am.Positions.Select(ct => (ct.contract, ct.position)), am.Positions, (pos, tradingClass, ps) => pos.contract.TradingClass == tradingClass && pos.position.Sign() == ps)
            .ForEach(comboPrice => {
              HandleMessage2(new { comboPrice.contract });
              comboPrice.contract.contract.ReqPriceSafe()
              .ToEnumerable()
              .ForEach(price => {
                HandleMessage($"Observed {comboPrice.contract} price:{price}");
                if(placeOrder) {
                  HandleMessage2($"Placing SELL order for{comboPrice.contract}");
                  am.OpenTrade(comboPrice.contract.contract, -1, price.ask.Avg(price.bid) * 0.55, 0, false, DateTime.MinValue);
                }
              });
              HandleMessage2($"ComboTrade Done ==================");
            });

          }
          void TestCurrentStraddles(int count, int gap) {
            var swCombo = Stopwatch.StartNew();
            Observable.Interval(TimeSpan.FromMilliseconds(1000))
            .Take(count)
            .SelectMany(pea => TestImpl()).Subscribe();
            IObservable<Unit> TestImpl() { // Combine positions
                                           //HandleMessage("Combos:");
              return am.CurrentStraddles("SPX", 1, 6, gap)
              .Select(ts => ts.Select(t => new {
                i = t.instrument,
                bid = t.bid.Round(2),
                ask = t.ask.Round(2),
                avg = t.ask.Avg(t.bid),
                time = t.time.ToString("HH:mm:ss"),
                delta = t.delta.Round(3),
                t.strikeAvg
              }))
             .Select(combos => {
               HandleMessage("Current Straddles:");
               combos.OrderBy(c => c.strikeAvg).ForEach(combo => HandleMessage(new { combo }));
               //HandleMessage($"Conbos done ======================================");
               HandleMessage($"Current Straddles done in {swCombo.ElapsedMilliseconds} ms =======================================");
               swCombo.Restart();
               return Unit.Default;
             })
            ;
              //HandleMessage($"Done in {swCombo.ElapsedMilliseconds} ms");
            }
          }
          void TestParsedCombos() {
            am.ComboTrades(1)
            .ToArray()
            .ToEnumerable()
            .ToArray()
            .ForEach(comboPrices => {
              HandleMessage2("ComboTrades Start");
              comboPrices.ForEach(comboPrice => HandleMessage2(new { comboPrice }));
              HandleMessage2($"ComboTrades Done ==================");
            });
          }
          IObservable<Unit> TestCombosTrades(int count) {
            return ibClient.PriceChangeObservable
            //.Throttle(TimeSpan.FromSeconds(0.1))
            //.DistinctUntilChanged(_ => am.Positions.Count)
            .Take(count)
            .Select(pea => {
              TestComboTradesImpl();
              return Unit.Default;
            });
            void TestComboTradesImpl() { // Combine positions
              var swCombo = Stopwatch.StartNew();
              am.ComboTrades(1)
              .ToArray()
              .ToEnumerable()
              .ToArray()
              .ForEach(comboPrices => {
                HandleMessage2("Matches: Start");
                comboPrices.ForEach(comboPrice => HandleMessage2(new { comboPrice }));
                HandleMessage2($"Matches: Done in {swCombo.ElapsedMilliseconds} ms =========================================");
              });
            }
          }
          IObservable<Unit> ProcessSymbols(int count) {
            //return ibClient.PriceChangeObservable.Sample(TimeSpan.FromSeconds(0.1))
            //.DistinctUntilChanged(_ => am.Positions.Count)
            return Observable.Interval(TimeSpan.FromMilliseconds(100))
            .Select(pea => {
              TestImpl();
              HandleMessage(nameof(ProcessSymbols) + " done =========================================================================");
              return Unit.Default;
            });
            void TestImpl() {
              var combos = symbols.Take(10).Buffer(10).Repeat(1).Select(b => b.Select(ProcessSymbol).Count(symbols.Length, new { }).ToArray())
              .Do(list => {
                //Passager.ThrowIf(() => list.Count != symbols.Length);
                HandleMessage2(new { sw.ElapsedMilliseconds });
                sw.Restart();
              })
              .ToList();
              sw.Stop();
              return;
              (from cls in combos
               from cl in cls
               from c in cl
               from o in c.options
               select o
               )
               .ToObservable()
               //.Do(_ => Thread.Sleep(200))
               .SelectMany(option => option.ReqPriceSafe().Select(p => (option, p)))
               .Subscribe(price => HandleMessage2($"Observed:{price}"));
            }
            IList<(Contract contract, Contract[] options)> ProcessSymbol(string symbol) {
              //HandleMessage(new { symbol } + "");
              // fw.AccountManager.BatterflyFactory("spx index").ToArray().ToEnumerable()

              //ibClient.ReqPriceMarket(symbol).ToEnumerable().Count(1, "ReqMarketPrice").ForEach(mp => HandleMessage($"{symbol}:{new { mp = mp.ToJson() }}"));

              //var cds = ibClient.ReqContractDetails(symbol);
              //HandleMessage($"{symbol}: {cds.Select(cd => cd.Summary).Flatter(",") }");
              return am.CurrentStraddles(symbol, 1, 4, 0)
              //.Do(t => t.options.ForEach(c => ibClient.SetOfferSubscription(c, _ => { })))
              .ToEnumerable()
              .ToArray()
              .Do(straddles => straddles.Count(4, new { }))
              .Concat()
              .Do(c => Passager.ThrowIf(() => !c.combo.contract.Instrument.Contains("[C-P]")))
              .Do(straddle => {
                HandleMessage2(new { straddle = straddle.combo.contract });
                //ibClient.SetOfferSubscription(straddle.combo.contract);
                straddle.combo.contract.ReqPriceSafe(2, false, double.NaN)
                    .Take(1)
                    .Subscribe(price => {
                      Passager.ThrowIf(() => price.ask <= 0 || price.bid <= 0);
                      HandleMessage2($"Observed:{straddle.instrument}{price}");
                    });
              })
              .Select(straddle => straddle.combo)
              .ToArray();
            }
          }
          (Contract contract, Contract[] options)[] TestStraddleds(string symbol, int gap) {
            var straddlesCount = 5;
            var expirationCount = 1;
            int expirationDaysSkip = 0;
            var price = ibClient.ReqContractDetailsCached(symbol).SelectMany(cd => cd.ReqPriceSafe().Select(p => p.ask.Avg(p.bid)).Do(mp => HandleMessage($"{symbol}:{new { mp }}")));
            var contracts = (from p in price
                             from str in fw.AccountManager.MakeStraddles(symbol, p, expirationDaysSkip, expirationCount, straddlesCount, gap)
                             select str)
            .ToEnumerable()
            .ToArray()
            .Count(straddlesCount * expirationCount, i => { Debugger.Break(); }, i => { Debugger.Break(); }, new { straddlesCount, expirationCount })
            .Do(c => Passager.ThrowIf(() => !c.contract.Instrument.Contains("[C-P]")))
            .ToArray();
            //Passager.ThrowIf(() => !IBClientCore.OptionChainCache.Count(1, new { }).Do(HandleMessage).Any(x => x.Value.tradingClass == "SPXW"));
            return contracts;
          }
          void StressTest() =>
            symbols.Take(10).Buffer(10).Repeat(10000).ToObservable()
            .Do(_ => { Thread.Sleep(100); })
            .SelectMany(b => b.Select(sym => ibClient.ReqContractDetailsCached(sym.ContractFactory())))
            .Merge()
            .Subscribe();
          #endregion
        });
      }

      HandleMessage("Press any key ...");
      Console.ReadKey();
      ibClient.Logout();
      HandleMessage("Press any key ...");
      Console.ReadKey();
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
      var tie = e.ExceptionObject as TypeInitializationException;
      if(tie != null) {
        HandleMessage(tie.InnerException);
        MessageBox.Show(tie.InnerException + "", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
      } else {
        ExceptionDispatchInfo.Capture(e.ExceptionObject as Exception).Throw();

      }
    }


    private static void LoadHistory(IBClientCore ibClient, IList<Contract> options) {
      var dateEnd = DateTime.Now;// new DateTime(DateTime.Parse("2017-06-21 12:00").Ticks, DateTimeKind.Local);
      DataMapDelegate<Rate> map = (DateTime date, double open, double high, double low, double close, long volume, int count) => new Rate(date, high, low, true);
      var counter = 0;
      if(options.Any()) {
        var c = options[0].ContractFactory();
        HandleMessage($"Loading History for {c}");
        new HistoryLoader<Rate>(ibClient, c, 0, dateEnd, TimeSpan.FromDays(5), TimeUnit.W, BarSize._3_mins, false,
           map,
           list => {
             HandleMessage($"{c} {new { list = new { list.Count, first = list.First().StartDate, last = list.Last().StartDate, Thread = Thread.CurrentThread.ManagedThreadId } }}");
             Debug.WriteLine(list.Csv());
           },
           dates => {
             HandleMessage($"{c} {new { dateStart = dates.FirstOrDefault(), dateEnd = dates.LastOrDefault(), reqCount = ++counter, Thread = Thread.CurrentThread.ManagedThreadId }}");
             HandleMessage(dates.Select(r => new { r.StartDate, r.PriceAvg }).ToTextOrTable("Rates:"));
           },
           exc => { });
      } else
        HandleMessage(new { options = options.ToJson() });
    }

    private static void OnPriceChanged(Price price) {
      HandleMessage(price.ToString());
    }

    #region Handle(Error/Massage)

    static IObserver<string> _tracer = InitTracer();
    static IObserver<string> InitTracer() {
      var s = new Subject<string>();
      s
        .SubscribeOn(TaskPoolScheduler.Default)
        .ObserveOn(TaskPoolScheduler.Default)
        .Subscribe(Console.WriteLine);
      return s;
    }

    public static void HandleMessage2<T>(T message) => HandleMessage(message, false);
    public static void HandleMessage<T>(T message) => HandleMessage(message, true);
    public static void HandleMessage<T>(T message, bool showTime = true) => HandleMessageImpl(TimeStamp(), message + "", showTime);
    static void HandleMessageImpl(string timeStamp, string message, bool showTime = true) {
      var m = (showTime ? timeStamp + " " : "") + $"{message}{t()}";
      //_tracer.OnNext(m);
      Console.WriteLine(m);
      string t() => message.Contains("~") ? "" : (DataManager.ShowThread());
    }
    private static void HandleMessageFake(string message) { }
    private static void HandleMessage(HistoricalDataMessage historicalDataEndMessage) {
      HandleMessage(historicalDataEndMessage + "");
    }
    private static void HandleMessage(HistoricalDataEndMessage historicalDataEndMessage) {
      HandleMessage(historicalDataEndMessage + "");
    }
    static string TimeStamp() => $"{DateTime.Now:mm:ss.fff}: ";
    #endregion
  }
}
