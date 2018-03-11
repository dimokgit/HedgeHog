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
        fw.AccountManager.ReqBidAsk(c.c)
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
      ibClient.ManagedAccountsObservable.Subscribe(s => {
        var symbol = "VXX";// "spx index";
        // fw.AccountManager.BatterflyFactory("spx index").ToArray().ToEnumerable()
        var mp=fw.AccountManager.ReqMarketPrice(symbol).ToEnumerable().ToArray();
        HandleMessage(new { mp=mp.ToJson() }+"");
        fw.AccountManager.ReqCurrentOptionsAsync(symbol).ToEnumerable()
        .ForEach(oc => HandleMessage(oc.k));
        var och = fw.AccountManager.BatterflyFactory(symbol).ToArray();
        OpenTrade(och);
        //.Merge(fw.AccountManager.BatterflyFactory("SPY").ToArray())
        //.Merge(fw.AccountManager.BatterflyFactory(fw, "VXX"))
        //.Subscribe(och => { OpenTrade(och); },exc=>HandleError(exc));
        //fw.AccountManager.ReqContractDetails(spx).Subscribe(cd => HandleMessage(cd.ToJson()), () => HandleMessage(new { ContractDetails = new { Completed = contract.LocalSymbol } } + ""));
      });

      if(ibClient.LogOn("127.0.0.1", 7497 + "", 102 + "", false)) {
        ibClient.SetOfferSubscription(contract);
        if(true) {
          var dateEnd = DateTime.Now;// new DateTime(DateTime.Parse("2017-06-21 12:00").Ticks, DateTimeKind.Local);
          HistoryLoader<Rate>.DataMapDelegate<Rate> map = (DateTime date, double open, double high, double low, double close, int volume, int count) => new Rate(date, high, low, true);
          var counter = 0;
          if(counter > 0)
            new HistoryLoader<Rate>(ibClient, contract, 1440 * 3, dateEnd, TimeSpan.FromDays(3), TimeUnit.D, BarSize._1_min,
               map,
               list => HandleMessage(new { list = new { list.Count, first = list.First().StartDate, last = list.Last().StartDate } } + ""),
               dates => HandleMessage(new { dateStart = dates.FirstOrDefault(), dateEnd = dates.LastOrDefault(), reqCount = ++counter } + ""),
               exc => { });
        }
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


    private static void HandleMessage(string message) {
      Console.WriteLine(DateTime.Now + ": " + message);
    }
    private static void HandleMessage(HistoricalDataMessage historicalDataEndMessage) {
      HandleMessage(historicalDataEndMessage + "");
    }
    private static void HandleMessage(HistoricalDataEndMessage historicalDataEndMessage) {
      HandleMessage(historicalDataEndMessage + "");
    }
    #endregion
  }
}
