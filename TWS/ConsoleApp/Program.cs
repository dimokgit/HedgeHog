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
      DataManager.DoShowRequestErrorDone = false;

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
      AccountManager.NoPositionsPlease = true;
      ibClient.ManagedAccountsObservable.Subscribe(s => {
        //ShowTrades(fw);
        //TestStraddleds();return;
        var option = "VXX   180329C00051500";
        var cds = ibClient.ReqContractDetailsAsync(option.ContractFactory()).ToEnumerable().ToArray();
        var symbols = new[] { "SPX", "VXX", "SPY" };
        //var timeOut = Observable.Return(0).Delay(TimeSpan.FromSeconds(100)).Timeout(TimeSpan.FromSeconds(15 * 1)).Subscribe();
        Stopwatch sw = Stopwatch.StartNew();
        if(true) {
          var combos = symbols.Take(10).Buffer(10).Repeat(1).Select(b => b.Select(ProcessSymbol).Count(3, new { }).ToArray())
          .Do(list => {
            //Passager.ThrowIf(() => list.Count != symbols.Length);
            HandleMessage(new { sw.ElapsedMilliseconds });
            sw.Restart();
          })
          .ToList();
          sw.Stop();
          (from cls in combos
           from cl in cls
           from c in cl
           from o in c.options
           select o
           )
           .ToObservable()
           //.Do(_ => Thread.Sleep(200))
           .SelectMany(c => ibClient.ReqPrice(c, 1, false))
           .Subscribe(price => HandleMessage($"Observed:{price}"));
        }
        //timeOut.Dispose();
        //combos.ForEach(combo => Passager.ThrowIf(() => combo.Count < 2));
        HandleMessage(nameof(ProcessSymbol) + " done =========================================================================");
        //Contract.Contracts.OrderBy(c => c + "").ForEach(cached => HandleMessage(new { cached }));
        //HandleMessage(nameof(ProcessSymbol) + " done =========================================================================");
        LoadHistory(ibClient, new[] { "spx".ContractFactory() });

        IList<(Contract contract, Contract[] options)> ProcessSymbol(string symbol) {
          //HandleMessage(new { symbol } + "");
          // fw.AccountManager.BatterflyFactory("spx index").ToArray().ToEnumerable()

          //ibClient.ReqPriceMarket(symbol).ToEnumerable().Count(1, "ReqMarketPrice").ForEach(mp => HandleMessage($"{symbol}:{new { mp = mp.ToJson() }}"));

          //var cds = ibClient.ReqContractDetails(symbol);
          //HandleMessage($"{symbol}: {cds.Select(cd => cd.Summary).Flatter(",") }");

          return TestStraddleds(symbol)
          .Do(t => t.options.ForEach(c => ibClient.SetOfferSubscription(c, _ => { })))
          .ToArray()
          .Do(burrefly => {
            HandleMessage(new { straddle = burrefly.contract });
            ibClient.SetOfferSubscription(burrefly.contract, _ => { });
            ibClient.ReqPrice(burrefly.contract, 1, false)
            .DefaultIfEmpty((burrefly.contract, double.NaN, double.NaN, DateTime.MinValue))
            .Take(1)
            .Subscribe(price => HandleMessage($"Observed:{price}"));
          }).ToArray();
        }
        (Contract contract, Contract[] options)[] TestStraddleds(string symbol) {
          var straddlesCount = 6;
          var expirationCount = 1;
          var price = ibClient.ReqPriceSafe(symbol).Select(p=>p.ask.Avg(p.bid)).Do(mp => HandleMessage($"{symbol}:{new { mp }}"));
          var contracts = (from p in price
                           from str in fw.AccountManager.MakeStraddle(symbol, p, expirationCount, straddlesCount)
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

    private static void ShowTrades(IBWraper fw) => fw.AccountManager.PositionsObservable
            .Distinct(p => p.contract.Key)
            .Subscribe(pos => {
              fw._ibClient.ReqPrice(pos.contract, 1, false)
              .Subscribe(_ => {
                var trades = (from trade in fw.GetTrades()
                              orderby trade.Pair
                              orderby trade.IsBuy descending
                              select new {
                                trade.Pair, trade.Position, trade.NetPL2, trade.Open, trade.Close,
                                PL = (trade.Close - trade.Open) * trade.Position
                              }
                             ).ToArray();
                //var straddles = (from trade in fw.GetTrades()
                //                 join c in Contract.Cache() on trade.Pair equals c.Key
                //                 select new { c, trade } into g0
                //                 group g0 by new { g0.c.Symbol, g0.c.Strike } into g
                //                 from strdl in g.OrderBy(v => v.c.Instrument)
                //                 .Buffer(2, 1)
                //                 .Where(b => b.Count == 2)
                //                 .Select(b => b.MashDiffs(x => x.c.Instrument))
                //                 select new { combo = strdl.mash, netPL = strdl.source.Select(x => x.trade).Gross() }
                //                 );
                HandleMessage($"Trades:\n{trades.Flatter("\n")}");
                HandleMessage($"Straddles:\n{fw.AccountManager.TradeStraddles().Flatter("\n")}");
              });
            });
    private static void LoadHistory(IBClientCore ibClient, IList<Contract> options) {
      var dateEnd = DateTime.Now;// new DateTime(DateTime.Parse("2017-06-21 12:00").Ticks, DateTimeKind.Local);
      HistoryLoader<Rate>.DataMapDelegate<Rate> map = (DateTime date, double open, double high, double low, double close, int volume, int count) => new Rate(date, high, low, true);
      var counter = 0;
      if(options.Any()) {
        var c = options[0].ContractFactory();
        new HistoryLoader<Rate>(ibClient, c, 1800 * 1, dateEnd, TimeSpan.FromDays(1), TimeUnit.S, BarSize._1_secs,
           map,
           list => {
             HandleMessage($"{c} {new { list = new { list.Count, first = list.First().StartDate, last = list.Last().StartDate, Thread.CurrentThread.ManagedThreadId } }}");
             Debug.WriteLine(list.Csv());
           },
           dates => HandleMessage($"{c} {new { dateStart = dates.FirstOrDefault(), dateEnd = dates.LastOrDefault(), reqCount = ++counter, Thread.CurrentThread.ManagedThreadId }}"),
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
    private static void HandleMessage<T>(T message) => HandleMessage(message + "");
    private static void HandleMessage(string message) {
      if(_tracePrefix.IsNullOrEmpty() || message.StartsWith(_tracePrefix))
        Console.WriteLine(DateTime.Now + ": " + message);
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
