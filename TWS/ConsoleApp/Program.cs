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
      void OpenTrade(IList<Contract> contracts) {
        HandleMessage(contracts.First().Key());
        //contracts.Take(1).ForEach(c => fw.AccountManager.OpenTrade(c, 10, 0, 0, "", false));
      }
      ibClient.ManagedAccounts += s => {
        var symbol = "spx index";
        var optionChain = (
          from cd in fw.AccountManager.ReqContractDetails(symbol.ContractFactory()).FirstAsync().Select(t => t.cd)
          from price in fw.AccountManager.ReqPrice(cd.Summary).FirstAsync()
          from och in fw.AccountManager.ReqSecDefOptParams(cd.Summary.LocalSymbol, "", cd.Summary.SecType, cd.Summary.ConId)
          select new { och.exchange, och.underlyingConId, och.tradingClass, och.multiplier, och.expirations, och.strikes, price, symbol = cd.Summary.Symbol, currency = cd.Summary.Currency }
        )
        .SkipWhile(t => t.expirations.First().FromTWSDateString() > DateTime.UtcNow.Date.AddDays(3))
        .FirstAsync();

        (from t in optionChain
         from exp in t.expirations.Take(1)
         from strikeMiddle in t.strikes.OrderBy(st => st.Abs(t.price)).Take(2).Select((strike, i) => new { strike, i })
         from strike in new[] { strikeMiddle.strike - 5, strikeMiddle.strike, strikeMiddle.strike + 5 }
         let option = MakeOptionSymbol(t.tradingClass, exp.FromTWSDateString(), strike, true)
         from o in fw.AccountManager.ReqContractDetails(ContractSamples.Option(option)).FirstAsync()
         select new { t.symbol, o.cd.Summary.Exchange, o.cd.Summary.ConId, o.cd.Summary.Currency, o.reqId, t.price, strikeMiddle.i }
         )
         .Buffer(6)
         .SelectMany(b => b.OrderBy(c => c.i).ThenBy(c => c.ConId).ToArray())
         .Buffer(3)
         .Do(b => HandleMessage(b.ToJson()))
         .Select(b => new { b[0].symbol, b[0].Exchange, b[0].Currency, conIds = b.Select(x => x.ConId).ToArray() })
         .Select(b => MakeButterfly(b.symbol, b.Exchange, b.Currency, b.conIds))
         .ToArray()
         .Subscribe(och => {
           OpenTrade(och);
           HandleMessage("Butterfly done");
         });
        //fw.AccountManager.ReqContractDetails(spx).Subscribe(cd => HandleMessage(cd.ToJson()), () => HandleMessage(new { ContractDetails = new { Completed = contract.LocalSymbol } } + ""));
      };

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
        } else {
          var sp500 = HedgeHog.Alice.Store.GlobalStorage.UseForexContext(c => c.SP500.Where(sp => sp.LoadRates).ToArray());
          var dateStart = DateTime.UtcNow.Date.ToLocalTime().AddMonths(-1).AddDays(-2);
          foreach(var sp in sp500.Select(b => b.Symbol)) {
            HedgeHog.Alice.Store.PriceHistory.AddTicks(fw, 1, sp, dateStart, o => HandleMessage(o + ""));
          }
        }
      }
      HandleMessage("Press any key ...");
      Console.ReadKey();
      ibClient.Logout();
      HandleMessage("Press any key ...");
      Console.ReadKey();
    }
    static Contract MakeButterfly(string symbol, string exchange, string currency, int[] conIds) {
      if(conIds.Zip(conIds.Skip(1)).Any(t => t.Item1 >= t.Item2))
        throw new Exception($"Butterfly legs are out of order:{string.Join(",", conIds)}");
      var c = new Contract() {
        Symbol = symbol,
        SecType = "BAG",
        Exchange = exchange,
        Currency = currency
      };
      var left = new ComboLeg() {
        ConId = conIds[0],
        Ratio = 1,
        Action = "BUY",
        Exchange = exchange
      };
      var middle = new ComboLeg() {
        ConId = conIds[1],
        Ratio = 2,
        Action = "SELL",
        Exchange = exchange
      };
      var right = new ComboLeg() {
        ConId = conIds[2],
        Ratio = 1,
        Action = "BUY",
        Exchange = exchange
      };
      c.ComboLegs = new List<ComboLeg> { left, middle, right };
      return c;
    }
    static string MakeOptionSymbol(string symbol, DateTime expiration, double strike, bool isCall) {
      var date = expiration.ToTWSOptionDateString();
      var cp = isCall ? "C" : "P";
      var price = strike.ToString("00000") + "000";
      return $"{symbol}  {date}{cp}{price}";
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
