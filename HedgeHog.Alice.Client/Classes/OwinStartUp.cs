using HedgeHog.Alice.Store;
using HedgeHog.Shared;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Cors;
using Microsoft.Owin.FileSystems;
using Microsoft.Owin.StaticFiles;
using Owin;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Client {
  public class StartUp {
    public string Pair { get { return "usdjpy"; } }
    List<string> Pairs = new List<string>() { "usdjpy", "eurusd" };
    double IntOrDouble(double d, double max = 10) {
      return d.Abs() > max ? d.ToInt() : d.Round(1);
    }
    public void Configuration(IAppBuilder app) {
      GlobalHost.Configuration.DefaultMessageBufferSize = 1;
      //GlobalHost.HubPipeline.AddModule(new MyHubPipelineModule());
      // Static content
      var fileSystem = new PhysicalFileSystem("./www");
      var fsOptions = new FileServerOptions {
        EnableDirectoryBrowsing = true,
        FileSystem = fileSystem
      };
      app.UseFileServer(fsOptions);

      // SignalR timer
      var remoteControl = App.container.GetExport<RemoteControlModel>();
      var trader = App.container.GetExportedValue<TraderModel>();

      var makeClienInfo = MonoidsCore.ToFunc((TradingMacro)null, tm => new {
        time = tm.ServerTime.ToString("HH:mm:ss"),
        prf = IntOrDouble(tm.CurrentGrossInPipTotal, 1),
        dur = TimeSpan.FromMinutes(tm.RatesDuration).ToString(@"hh\:mm"),
        hgt = tm.RatesHeightInPips.ToInt() + "/" + tm.BuySellHeightInPips.ToInt(),
        rsdMin = tm.RatesStDevMinInPips,
        equity = remoteControl.Value.MasterModel.AccountModel.Equity
        //closed = trader.Value.ClosedTrades.OrderByDescending(t=>t.TimeClose).Take(3).Select(t => new { })
      });
      IDisposable priceChanged = null;
      var propName = Lib.GetLambda<TraderModel>(x => x.PriceChanged);
      trader.PropertyChanged += (object sender, PropertyChangedEventArgs e) => {
        if (e.PropertyName != propName) return;
        if (priceChanged != null) priceChanged.Dispose();
        var trm = (TraderModel)sender;
        priceChanged = trm.PriceChanged
          .Select(x => x.EventArgs.Pair.Replace("/", "").ToLower())
          .Where(pair => Pairs.Contains(pair))
          .Subscribe(pair => {
            try {
              GlobalHost.ConnectionManager.GetHubContext<MyHub>()
                .Clients.All.priceChanged(pair);
            } catch (Exception exc) {
              GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
            }
          });
      };
      app.UseCors(CorsOptions.AllowAll);
      app.Use((context, next) => {
        try {
          if (context == null)
            App.SetSignalRSubjectSubject(() => {
              var tm = remoteControl.Value.TradingMacrosCopy.FirstOrDefault(t => t.PairPlain == Pair);
              if (tm != null) {
                try {
                  GlobalHost.ConnectionManager.GetHubContext<MyHub>()
                    .Clients.All.addMessage(makeClienInfo(tm));
                } catch (Exception exc) {
                  GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
                }
              }
            });

          if (context.Request.Path.Value.ToLower() == "/hello") {
            return context.Response.WriteAsync("privet:" + DateTimeOffset.Now);
          }
          var path = context.Request.Path.Value.ToLower().Substring(1);
          var rc = remoteControl.Value;
          {
            var tm = rc.TradingMacrosCopy.FirstOrDefault(t => t.PairPlain == path);
            if (tm != null) {
              context.Response.ContentType = "image/png";
              return context.Response.WriteAsync(rc.GetCharter(tm).GetPng());
            }
          }
          {
            var tm = rc.TradingMacrosCopy.Skip(1).FirstOrDefault(t => t.PairPlain == path.Replace("2", ""));
            if (tm != null) {
              context.Response.ContentType = "image/png";
              return context.Response.WriteAsync(rc.GetCharter(tm).GetPng());
            }
          }
        } catch (Exception exc) {
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        }
        return next();
      });
      // SignalR
      try {
        var hubConfiguration = new HubConfiguration();
        hubConfiguration.EnableDetailedErrors = true;
        app.MapSignalR(hubConfiguration);
      } catch (InvalidOperationException exc) {
        if (!exc.Message.StartsWith("Counter"))
          throw;
      }
    }
  }
  public class MyHub : Hub {
    Lazy<RemoteControlModel> remoteControl;
    static Exception Log { set { GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(value)); } }
    static ISubject<Action> _AskRatesSubject;
    static ISubject<Action> AskRatesSubject {
      get { return _AskRatesSubject; }
    }
    static ISubject<Action> _AskRates2Subject;
    static ISubject<Action> AskRates2Subject {
      get { return _AskRates2Subject; }
    }
    static MyHub() {
      _AskRatesSubject = new Subject<Action>();
      _AskRatesSubject.InitBufferedObservable<Action>(exc => Log = exc);
      _AskRatesSubject.Subscribe(a => a());
      _AskRates2Subject = new Subject<Action>();
      _AskRates2Subject.InitBufferedObservable<Action>(exc => Log = exc);
      _AskRates2Subject.Subscribe(a => a());
    }
    public MyHub() {
      remoteControl = App.container.GetExport<RemoteControlModel>();
    }
    public void AskChangedPrice(string pair) {
      var makeClienInfo = MonoidsCore.ToFunc((TradingMacro)null, tm => new {
        time = tm.ServerTime.ToString("HH:mm:ss"),
        prf = IntOrDouble(tm.CurrentGrossInPipTotal, 1),
        otg= IntOrDouble(tm.OpenTradesGross2InPips, 1),
        tps = tm.TicksPerSecondAverage.Round(1),
        dur = TimeSpan.FromMinutes(tm.RatesDuration).ToString(@"hh\:mm"),
        hgt = tm.RatesHeightInPips.ToInt() + "/" + tm.BuySellHeightInPips.ToInt(),
        rsdMin = tm.RatesStDevMinInPips,
        equity = remoteControl.Value.MasterModel.AccountModel.Equity.Round(0),
        price = new { ask = tm.CurrentPrice.Ask, bid = tm.CurrentPrice.Bid },
        tci = GetTradeConditionsInfo(tm)
        //closed = trader.Value.ClosedTrades.OrderByDescending(t=>t.TimeClose).Take(3).Select(t => new { })
      });
      var tm2 = remoteControl.Value.TradingMacrosCopy.FirstOrDefault(t => t.PairPlain == pair);
      if (tm2 != null) {
        try {
          Clients.Caller.addMessage(makeClienInfo(tm2));
        } catch (Exception exc) {
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        }
      }
    }
    public string[] ReadTradingConditions(string pair) {
      return UseTradingMacro(pair, tm => tm.TradeConditionsAllInfo((tc, name) => name).ToArray());
    }
    public string[] GetTradingConditions(string pair) {
      return UseTradingMacro(pair, tm => tm.TradeConditionsInfo((tc, name) => name).ToArray());
    }
    public void SetTradingConditions(string pair, string[] names) {
      UseTradingMacro(pair, tm => tm.TradeConditionsSet(names));
    }
    public Dictionary<string, bool> GetTradingConditionsInfo(string pair) {
      return UseTradingMacro(pair, tm => GetTradeConditionsInfo(tm));
    }

    private static Dictionary<string, bool> GetTradeConditionsInfo(TradingMacro tm) {
      return tm.TradeConditionsInfo((d, c) => new { c, d = d() }).ToDictionary(x => x.c, x => x.d);
    }
    public void Send(string pair, string newInyterval) {
      try {
        App.SignalRInterval = int.Parse(newInyterval);
        App.ResetSignalR();
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
      }
    }
    public void CloseTrades(string pair) {
      try {
        GetTradingMacro(pair).CloseTrades("SignalR");
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
      }
    }
    public void MoveTradeLevel(string pair, bool isBuy, double pips) {
      try {
        GetTradingMacro(pair).MoveBuySellLeve(isBuy, pips);
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
      }
    }
    public void ManualToggle(string pair) {
      try {
        GetTradingMacro(pair).ResetSuppResesInManual();
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
      }
    }
    public void ToggleStartDate(string pair,int chartNumber) {
      UseTradingMacro(pair, chartNumber, tm => tm.ToggleCorridorStartDate());
    }
    public void ToggleIsActive(string pair,int chartNumber) {
      UseTradingMacro(pair, chartNumber, tm => tm.ToggleIsActive());
    }
    public void FlipTradeLevels(string pair) {
      UseTradingMacro(pair, tm => tm.FlipTradeLevels());
    }
    public void WrapTradeInCorridor(string pair) {
      UseTradingMacro(pair, tm => tm.WrapTradeInCorridor());
    }
    public void WrapCurrentPriceInCorridor(string pair) {
      UseTradingMacro(pair, tm => tm.WrapCurrentPriceInCorridor());
    }
    public void SetAlwaysOn(string pair) {
      UseTradingMacro(pair, tm => tm.SetAlwaysOn());
    }
    public void Buy(string pair) {
      UseTradingMacro(pair, tm => tm.OpenTrade(true, tm.LotSizeByLossBuy, "web"));
    }
    public void Sell(string pair) {
      UseTradingMacro(pair, tm => tm.OpenTrade(false, tm.LotSizeByLossBuy, "web"));
    }
    public void SetRsdTreshold(string pair, int pips) {
      UseTradingMacro(pair, tm => tm.RatesStDevMinInPips = pips);
    }
    public ExpandoObject AskRates(int charterWidth, DateTimeOffset startDate, DateTimeOffset endDate, string pair) {
      return UseTradingMacro(pair, 0, tm => remoteControl.Value.ServeChart(charterWidth, startDate, endDate, tm));
    }
    public ExpandoObject AskRates2(int charterWidth, DateTimeOffset startDate, DateTimeOffset endDate, string pair) {
      try {
        var tm = GetTradingMacro(pair, 1);
        return tm != null && tm.IsActive
          ? remoteControl.Value.ServeChart(charterWidth, startDate, endDate, tm)
          : new ExpandoObject();
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }
    public void SetPresetTradeLevels(string pair, TradeLevelsPreset presetLevels, object isBuy) {
      bool? b = isBuy == null ? null : (bool?)isBuy;
      UseTradingMacro(pair, tm => tm.SetTradeLevelsPreset(presetLevels, b));
    }
    public void SetTradeLevel(string pair, bool isBuy,int level) {
      UseTradingMacro(pair, tm => {
        if (isBuy) {
          tm.LevelBuyBy = (TradeLevelBy)level;
        } else {
          tm.LevelSellBy = (TradeLevelBy)level;
        }
      });
    }
    public void SetTradeCloseLevel(string pair, bool isBuy, int level) {
      UseTradingMacro(pair, tm => {
        if (isBuy) {
          tm.LevelBuyCloseBy = (TradeLevelBy)level;
        } else {
          tm.LevelSellCloseBy = (TradeLevelBy)level;
        }
      });
    }
    public object[] ReadNews() {
      var date = DateTimeOffset.UtcNow.AddMinutes(-60);
      var countries = new[] { "USD", "GBP", "EUR", "JPY" };
      var events = GlobalStorage.UseForexContext(c => c.Event__News.Where(en => en.Time > date && countries.Contains(en.Country)).ToArray());
      var events2 = events.ToArray(en => new { en.Time, en.Name, en.Level, en.Country });
      return events2;
    }
    public Dictionary<string, int> ReadTradeLevelBys() {
      return Enum.GetValues(typeof(TradeLevelBy)).Cast<TradeLevelBy>().ToDictionary(t => t.ToString(), t => (int)t);
    }
    #region TradeSettings
    public void SaveTradeSettings(string pair, ExpandoObject ts) {
      UseTradingMacro(pair, tm => {
        var props = (IDictionary<string, object>)ts;
        props.ForEach(kv => {
          tm.SetProperty(kv.Key, kv.Value);
        });
      });
    }
    public ExpandoObject ReadTradeSettings(string pair) {
      return UseTradingMacro(pair, tm => {
        var e = (IDictionary<string, object>)new ExpandoObject();
        tm.GetPropertiesByAttibute<WwwSettingAttribute>(_ => true)
          .Select(x => new { x.Item1.Group, p = x.Item2 })
          .OrderBy(x => x.p.Name)
          .OrderBy(x => x.Group)
          .ForEach(x => e.Add(x.p.Name, new { v = x.p.GetValue(tm), g = x.Group }));
        return e as ExpandoObject;
      });
    }
    public Trade[] ReadClosedTrades(string pair) {
      try {
        return remoteControl.Value.GetClosedTrades(pair);
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }
    public int MoveCorridorWavesCount(string pair, int chartNumber, int step) {
      return UseTradingMacro(pair, chartNumber, tm => {
        tm.IsTradingActive = false;
        return tm.CorridorWaveCount = tm.CorridorWaveCount + step;
        //return tm.PriceCmaLevels_ = (tm.PriceCmaLevels_ + step).Max(1).Min(2).Round(1);
      });
    }
    #endregion
    double IntOrDouble(double d, double max = 10) {
      return d.Abs() > max ? d.ToInt() : d.Round(1);
    }
    public void ResetPlotter(string pair) {
      var tm = GetTradingMacro(pair);
      var trader = App.container.GetExport<TraderModel>();
      if (tm != null)
        Clients.All.resetPlotter(new {
          time = tm.ServerTime.ToString("HH:mm:ss"),
          duration = TimeSpan.FromMinutes(tm.RatesDuration).ToString(@"hh\:mm"),
          count = tm.BarsCountCalc,
          stDev = IntOrDouble(tm.StDevByHeightInPips) + "/" + IntOrDouble(tm.StDevByPriceAvgInPips),
          height = tm.RatesHeightInPips.ToInt(),
          profit = IntOrDouble(tm.CurrentGrossInPipTotal),
          closed = trader.Value.ClosedTrades.Select(t => new { })
        });
    }
    public void SetTradeCount(string pair, int tradeCount) {
      GetTradingMacro(pair, tm => tm.SetTradeCount(tradeCount));
    }
    public void StopTrades(string pair) { SetCanTrade(pair, false, null); }
    public void StartTrades(string pair, bool isBuy) { SetCanTrade(pair, true, isBuy); }
    void SetCanTrade(string pair, bool canTrade, bool? isBuy) {
      var tm = GetTradingMacro(pair);
      if (tm != null)
        tm.SetCanTrade(canTrade, isBuy);
    }
    private void GetTradingMacro(string pair, Action<TradingMacro> action) {
      var tm = GetTradingMacro(pair);
      if (tm != null) action(tm);
    }
    private TradingMacro GetTradingMacro(string pair, int chartNum = 0) {
      var rc = remoteControl.Value;
      var tm = rc.TradingMacrosCopy.Skip(chartNum).FirstOrDefault(t => t.PairPlain == pair);
      return tm;
    }
    T UseTradingMacro<T>(string pair, Func<TradingMacro, T> func) {
      return UseTradingMacro(pair, 0, func);
    }
    void UseTradingMacro(string pair, Action<TradingMacro> action) {
      UseTradingMacro(pair, 0, action);
    }
    void UseTradingMacro(string pair, int chartNum, Action<TradingMacro> action) {
      try {
        action(GetTradingMacro(pair, chartNum));
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
      }
    }
    T UseTradingMacro<T>(string pair, int chartNum, Func<TradingMacro, T> func) {
      try {
        return func(GetTradingMacro(pair, chartNum));
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }

  }
}
