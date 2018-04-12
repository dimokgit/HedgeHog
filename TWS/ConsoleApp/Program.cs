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
using AutoMapper;
using System.Reactive.Disposables;
using System.Reactive.Concurrency;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reactive;

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
      ShowWindow(ThisConsole, MAXIMIZE);
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
      var es = ContractSamples.ContractFactory("ESM7");
      var vx = ContractSamples.ContractFactory("VXH8");
      var spy = ContractSamples.ContractFactory("SPY");
      var svxy = ContractSamples.ContractFactory("SVXY");
      //var opt = ContractSamples.Option("SPX","20180305",2695,true,"SPXW");
      var opt = ContractSamples.Option("SPXW  180305C02680000");
      var contract = spy;
      AccountManager.NoPositionsPlease = false;
      DataManager.DoShowRequestErrorDone = true;
      ibClient.ManagedAccountsObservable.Subscribe(s => {
        HandleMessage($"{Thread.CurrentThread.ManagedThreadId}");
        var am = fw.AccountManager;
        var cdSPY = ibClient.ReqContractDetailsCached("SPY").ToEnumerable().ToArray();
        var cdSPY2 = ibClient.ReqContractDetailsCached("SPY").ToEnumerable().ToArray();
        Task.Delay(2000).ContinueWith(_ => {
          TestMakeBullPut(false);
        }); return;
        LoadHistory(ibClient, new[] { "spy".ContractFactory() });
        TestParsedCombos();
        TestCurrentStraddles(1, 1);
        TestCurrentStraddles(1, 1); return;
        TestCombosTrades(10).Subscribe(); return;
        var cds = ibClient.ReqContractDetailsAsync("VXX   180329C00051500".ContractFactory()).ToEnumerable().Count(0, new { }).ToArray();
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

        #region Local Tests
        void TestMakeBullPut(bool placeOrder) {
          HandleMessage2("MakeBullPut Start");
          am.CurrentBullPuts("SPX",double.NaN,1,3,0)
          .ToEnumerable()
          .Concat()
          .ForEach(comboPrice => {
            HandleMessage2(new { comboPrice.combo.contract });
            ibClient.ReqPriceSafe(comboPrice.combo.contract, 4, true)
            .ToEnumerable()
            .ForEach(price => {
              HandleMessage($"Observed {comboPrice.combo.contract} price:{price}");
              if(placeOrder) {
                HandleMessage2($"Placing SELL order for{comboPrice.combo.contract}");
                am.OpenTrade(comboPrice.combo.contract, -1, price.ask.Avg(price.bid) * 0.55, false);
              }
            });
            HandleMessage2($"MakeBullPut Done ==================");
          });

        }
        void TestMakeComboAll(bool placeOrder) {
          HandleMessage2("ComboTrade Start");
          AccountManager.MakeComboAll(am.Positions.Select(ct => (ct.contract, ct.position)), am.Positions, (pos, tradingClass) => pos.contract.TradingClass == tradingClass)
          .ForEach(comboPrice => {
            HandleMessage2(new { comboPrice.contract });
            ibClient.ReqPriceSafe(comboPrice.contract, 4, true)
            .ToEnumerable()
            .ForEach(price => {
              HandleMessage($"Observed {comboPrice.contract} price:{price}");
              if(placeOrder) {
                HandleMessage2($"Placing SELL order for{comboPrice.contract}");
                am.OpenTrade(comboPrice.contract, -1, price.ask.Avg(price.bid) * 0.55, false);
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
             .SelectMany(option => ibClient.ReqPriceSafe(option, 1, false).Select(p => (option, p)))
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
            .Do(c => Passager.ThrowIf(() => !c.combo.contract.Key.Contains("[C-P]")))
            .Do(straddle => {
              HandleMessage2(new { straddle = straddle.combo.contract });
              //ibClient.SetOfferSubscription(straddle.combo.contract);
              ibClient.ReqPriceSafe(straddle.combo.contract, 2, false, double.NaN)
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
          var price = ibClient.ReqContractDetailsCached(symbol).SelectMany(cd => ibClient.ReqPriceSafe(cd.Summary, 1, true).Select(p => p.ask.Avg(p.bid)).Do(mp => HandleMessage($"{symbol}:{new { mp }}")));
          var contracts = (from p in price
                           from str in fw.AccountManager.MakeStraddles(symbol, p, expirationDaysSkip, expirationCount, straddlesCount, gap)
                           select str)
          .ToEnumerable()
          .ToArray()
          .Count(straddlesCount * expirationCount, i => { Debugger.Break(); }, i => { Debugger.Break(); }, new { straddlesCount, expirationCount })
          .Do(c => Passager.ThrowIf(() => !c.contract.Key.Contains("[C-P]")))
          .ToArray();
          //Passager.ThrowIf(() => !IBClientCore.OptionChainCache.Count(1, new { }).Do(HandleMessage).Any(x => x.Value.tradingClass == "SPXW"));
          return contracts;
        }
        void StressTest() =>
          symbols.Take(10).Buffer(10).Repeat(10000).ToObservable()
          .Do(_ => { Thread.Sleep(100); })
          .SelectMany(b => b.Select(sym => ibClient.ReqContractDetailsAsync(sym.ContractFactory())))
          .Merge()
          .Subscribe();
        #endregion
      });

      if(ibClient.LogOn("127.0.0.1", 7497 + "", 10 + "", false)) {
        //ibClient.SetOfferSubscription(contract);
        //else {
        //  var sp500 = HedgeHog.Alice.Store.GlobalStorage.UseForexContext(c => c.SP500.Where(sp => sp.LoadRates).ToArray());
        //  var dateStart = DateTime.UtcNow.Date.ToLocalTime().AddMonths(-1).AddDays(-2);
        //  foreach(var sp in sp500.Select(b => b.Symbol)) {
        //    HedgeHog.Alice.Store.PriceHistory.AddTicks(fw, 1, sp, dateStart, o => HandleMessage(o + ""));
        //  }
        //}
      }
      HandleMessage("Press any key ...");
      Console.ReadKey();
      ibClient.Logout();
      HandleMessage("Press any key ...");
      Console.ReadKey();
    }

    private static void LoadHistory(IBClientCore ibClient, IList<Contract> options) {
      var dateEnd = DateTime.Now;// new DateTime(DateTime.Parse("2017-06-21 12:00").Ticks, DateTimeKind.Local);
      HistoryLoader<Rate>.DataMapDelegate<Rate> map = (DateTime date, double open, double high, double low, double close, int volume, int count) => new Rate(date, high, low, true);
      var counter = 0;
      if(options.Any()) {
        var c = options[0].ContractFactory();
        HandleMessage($"Loading History for {c}");
        new HistoryLoader<Rate>(ibClient, c, 1800 * 1, dateEnd, TimeSpan.FromDays(1), TimeUnit.S, BarSize._1_secs,
           map,
           list => {
             HandleMessage($"{c} {new { list = new { list.Count, first = list.First().StartDate, last = list.Last().StartDate, Thread = Thread.CurrentThread.ManagedThreadId } }}");
             Debug.WriteLine(list.Csv());
           },
           dates => HandleMessage($"{c} {new { dateStart = dates.FirstOrDefault(), dateEnd = dates.LastOrDefault(), reqCount = ++counter, Thread = Thread.CurrentThread.ManagedThreadId }}"),
           exc => { });
      } else
        HandleMessage(new { options = options.ToJson() });
    }

    private static void OnPriceChanged(Price price) {
      HandleMessage(price.ToString());
    }

    #region Handle(Error/Massage)
    static void HandleError(Exception ex) {
      HandleError(-1, -1, "", ex);
    }

    static void HandleError(int id, int errorCode, string str, Exception ex) {
      if(ex != null)
        Console.Error.WriteLine("Error: " + ex);
      else if(id == 0 || errorCode == 0)
        Console.Error.WriteLine("Error: " + str + "\n");
      else
        Console.Error.WriteLine(new ErrorMessage(id, errorCode, str));
    }

    static readonly string _tracePrefix;// = "OnTickPrice";
    private static void HandleMessage2<T>(T message) => HandleMessage(message + "", false);
    private static void HandleMessage<T>(T message, bool showTime = true) => HandleMessage(message + "", showTime);
    private static void HandleMessage(string message, bool showTime = true) {
      if(_tracePrefix.IsNullOrEmpty() || message.StartsWith(_tracePrefix))
        if(showTime)
          Console.WriteLine($"{DateTime.Now:mm:ss.f}: {message}");
        else
          Console.WriteLine(message);
    }
    private static void HandleMessageFake(string message) { }
    private static void HandleMessage(HistoricalDataMessage historicalDataEndMessage) {
      HandleMessage(historicalDataEndMessage + "");
    }
    private static void HandleMessage(HistoricalDataEndMessage historicalDataEndMessage) {
      HandleMessage(historicalDataEndMessage + "");
    }
    #endregion
  }
}
