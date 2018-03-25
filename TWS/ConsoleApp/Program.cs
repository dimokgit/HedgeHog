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

namespace ConsoleApp {
  class Program {
    static void Main(string[] args) {
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
      void OpenTrade(IList<(string k, Contract c)> contracts) {
        HandleMessage("\n" + string.Join("\n", contracts));
        contracts.Take(1).ForEach(c =>
        ibClient.ReqBidAsk(c.c)
          .Subscribe(p => HandleMessage(new { c, p } + "")));
        HandleMessage($"Butterflys {contracts.Count()} are done");
        //contracts.Take(1).ForEach(c => fw.AccountManager.OpenTrade(c.c, 10));
        //var counter = 0;
        //var dateEnd = DateTime.Now;// new DateTime(DateTime.Parse("2017-06-21 12:00").Ticks, DateTimeKind.Local);
        //HistoryLoader<Rate>.DataMapDelegate<Rate> map = (DateTime date, double open, double high, double low, double close, int volume, int count) => new Rate(date, high, low, true);
        //new HistoryLoader<Rate>(ibClient, contracts.First().c, 1440 * 3, dateEnd, TimeSpan.FromDays(4), TimeUnit.D, BarSize._1_min,
        //  map,
        //  list => HandleMessage(new { list = new { list.Count, first = list.First().StartDate, last = list.Last().StartDate } } + ""),
        //  dates => HandleMessage(new { dateStart = dates.FirstOrDefault(), dateEnd = dates.LastOrDefault(), reqCount = ++counter } + ""),
        //  exc => { });

      }
      IList<Contract> options = new Contract[0];
      ibClient.ManagedAccountsObservable.Subscribe(s => {
        var symbols = new[] { "VXX" };//, "SPY", "spx" };
        void ProcessSymbol(string symbol) {
          //HandleMessage(new { symbol } + "");
          // fw.AccountManager.BatterflyFactory("spx index").ToArray().ToEnumerable()

          ibClient.ReqMarketPrice(symbol).ToEnumerable()
          .Count(1, "ReqMarketPrice")
          .ForEach(mp => HandleMessage($"{symbol}:{new { mp = mp.ToJson() }}"));

          //var cds = ibClient.ReqContractDetails(symbol);
          //HandleMessage($"{symbol}: {cds.Select(cd => cd.Summary).Flatter(",") }");

          options = fw.AccountManager.MakeButterflies(symbol)
          .Merge(fw.AccountManager.MakeStraddle(symbol))
          .ToEnumerable()
          .Select(c => c.contract)
          .ToArray()
          .Do(burrefly => {
            HandleMessage(new { burrefly });
            ibClient.SetOfferSubscription(burrefly);
            ibClient.ReqPrice(burrefly, IBClientCore.TickType.MarketPrice)
            .Subscribe(price => HandleMessage(new { burrefly, price }));
          }).ToArray();
        }
        symbols.Take(10).Repeat(1).ForEach(ProcessSymbol);
        HandleMessage(nameof(ProcessSymbol) + " done =========================================================================");
        LoadHistory(ibClient, options);

        //var och = fw.AccountManager.BatterflyFactory(symbol).ToArray();
        //OpenTrade(och);

        //.Merge(fw.AccountManager.BatterflyFactory("SPY").ToArray())
        //.Merge(fw.AccountManager.BatterflyFactory(fw, "VXX"))
        //.Subscribe(och => { OpenTrade(och); },exc=>HandleError(exc));
        //fw.AccountManager.ReqContractDetails(spx).Subscribe(cd => HandleMessage(cd.ToJson()), () => HandleMessage(new { ContractDetails = new { Completed = contract.LocalSymbol } } + ""));
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


    private static void HandleMessage<T>(T message) => Console.WriteLine(DateTime.Now + ": " + message);
    private static void HandleMessage(string message) => Console.WriteLine(DateTime.Now + ": " + message);
    private static void HandleMessage(HistoricalDataMessage historicalDataEndMessage) {
      HandleMessage(historicalDataEndMessage + "");
    }
    private static void HandleMessage(HistoricalDataEndMessage historicalDataEndMessage) {
      HandleMessage(historicalDataEndMessage + "");
    }
    #endregion
  }
}
