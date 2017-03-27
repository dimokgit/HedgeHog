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

namespace ConsoleApp {
  class Program {
    static void Main(string[] args) {
      int _nextValidId = 0;

      var ibClient = IBClientCore.Create(o => HandleMessage(o + ""));
      ibClient.NextValidId += id => _nextValidId = id;
      ibClient.CurrentTime += time => HandleMessage("Current Time: " + ibClient.ServerTime + "\n");

      var coreFx = ibClient as ICoreFX;
      coreFx.LoginError += HandleError;
      coreFx.SubscribeToPropertyChanged(ibc => ibc.SessionStatus, ibc => HandleMessage(new { ibc.SessionStatus } + ""));
      //ibClient.PriceChanged += OnPriceChanged;

      var usdJpi = ContractSamples.FxContract("usd/jpy");
      var gold = ContractSamples.Commodity("XAUUSD");
      if(ibClient.LogOn("", 7497 + "", 2 + "", false)) {
        ibClient.SetOfferSubscription(gold.Instrument);
        var dateEnd = new DateTime( DateTime.Parse("2017-03-08 12:00").Ticks, DateTimeKind.Local);
        var counter = 0;
        HistoryLoader<Rate>.DataMapDelegate<Rate> map = (DateTime date, double open, double high, double low, double close, int volume, int count) => new Rate(date, high, low, true);
        new HistoryLoader<Rate>(ibClient, gold, dateEnd, TimeSpan.FromHours(4), TimeUnit.S, BarSize._1_secs,
           map,
           list => HandleMessage(new { list = new { list.Count, first = list.First().StartDate, last = list.Last().StartDate } } + ""),
           dates => HandleMessage(new { dateStart = dates.FirstOrDefault(), dateEnd = dates.LastOrDefault(), reqCount = ++counter } + ""),
           exc => HandleError(exc));
      }
      HandleMessage("Press any key ...");
      Console.ReadKey();
      ibClient.Logout();
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
