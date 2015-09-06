using HedgeHog.Alice.Store;
using HedgeHog.Shared;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin;
using Microsoft.Owin.Cors;
using Microsoft.Owin.FileSystems;
using Microsoft.Owin.StaticFiles;
using Owin;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
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

      //HttpListener listener = (HttpListener)app.Properties["System.Net.HttpListener"];
      //listener.AuthenticationSchemes = AuthenticationSchemes.Ntlm | AuthenticationSchemes.Basic;

      var fileSystem = new PhysicalFileSystem("./www");
      var fsOptions = new FileServerOptions {
        EnableDirectoryBrowsing = true,
        FileSystem = fileSystem
      };
      app.UseFileServer(fsOptions);

      // SignalR timer
      Func<IHubContext> myHub = () => GlobalHost.ConnectionManager.GetHubContext<MyHub>();
      var remoteControl = App.container.GetExport<RemoteControlModel>();
      IDisposable priceChanged = null;
      IDisposable tradesChanged = null;
      {
        var trader = App.container.GetExportedValue<TraderModel>();

        NewThreadScheduler.Default.Schedule(TimeSpan.FromSeconds(1), () => {
          priceChanged = trader.PriceChanged
            .Select(x => x.EventArgs.Pair.Replace("/", "").ToLower())
            .Where(pair => Pairs.Contains(pair))
            .Subscribe(pair => {
              try {
                myHub().Clients.All.priceChanged(pair);
              } catch (Exception exc) {
                GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
              }
            });
          tradesChanged =
            trader.TradeAdded.Select(x => x.EventArgs.Trade.Pair.Replace("/", "").ToLower())
            .Merge(
            trader.TradeRemoved.Select(x => x.EventArgs.MasterTrade.Pair.Replace("/", "").ToLower()))
            .Where(pair => Pairs.Contains(pair))
            .Subscribe(pair => {
              try {
                myHub().Clients.All.tradesChanged(pair);
              } catch (Exception exc) {
                GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
              }
            });
        });
      }
      app.UseCors(CorsOptions.AllowAll);
      app.Use((context, next) => {
        try {
          if (context == null)
            App.SetSignalRSubjectSubject(() => {
              var tm = remoteControl.Value.TradingMacrosCopy.FirstOrDefault(t => t.PairPlain == Pair);
              if (tm != null) {
                try {
                  GlobalHost.ConnectionManager.GetHubContext<MyHub>()
                    .Clients.All.addMessage();
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
      //app.Use((context, next) => {
      //  return new GZipMiddleware(d => next()).Invoke(context.Environment);
      //});
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
    class SendChartBuffer : AsyncBuffer<SendChartBuffer, Action> {
      protected override Action PushImpl(Action action) {
        return action;
      }
    }
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
    //http://forex.timezoneconverter.com/?timezone=GMT&refresh=5
    static Dictionary<string, DateTimeOffset> _marketHours = new Dictionary<string, DateTimeOffset> {
    { "Frankfurt", DateTimeOffset.Parse("6:00 +0:00") } ,
    { "London", DateTimeOffset.Parse("07:00 +0:00") } ,
    { "New York", DateTimeOffset.Parse("12:00 +0:00") } ,
    { "Sydney", DateTimeOffset.Parse("22:00 +0:00") } ,
    { "Tokyo", DateTimeOffset.Parse("23:00 +0:00") } 
    };
    static List<string> _marketHoursSet = new List<string>();
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
      App.WwwMessageWarning.Do(wm => {
        Clients.All.message(wm);
      });
      App.WwwMessageWarning.Clear();
    }
    bool IsLocalRequest {
      get {
        return (Context.Request.Environment["server.LocalIpAddress"] + "") == (Context.Request.Environment["server.RemoteIpAddress"] + "");
      }
    }
    bool IsUserTrader() {
      return true;
      return IsLocalRequest ||
        Context.User.Identity.Name.Split('\\').DefaultIfEmpty("").Last().ToLower() == "dimon";
    }
    void TestUserAsTrader() {
      if (!IsUserTrader())
        throw new UnauthorizedAccessException("This feature is for traders only.");
    }
    static DateTime _newsReadLastDate = DateTime.MinValue;
    static List<DateTimeOffset> _newsDates = new List<DateTimeOffset>();
    public object AskChangedPrice(string pair) {
      var tm0 = UseTradingMacro(pair, tm => tm, false);
      var tm1 = UseTradingMacro(pair, 1, tm => tm, false);
      var tmTrader = UseTradingMacro(pair, tm => tm.IsTrader, false).DefaultIfEmpty(tm0).Single();

      #region marketHours
      if (!tmTrader.IsInVitualTrading)
        _marketHours
          .Where(mh => !_marketHoursSet.Contains(mh.Key))
          .Where(mh => (tmTrader.ServerTime.ToUniversalTime().TimeOfDay - mh.Value.TimeOfDay).TotalMinutes.Between(-15, 0))
          .Do(mh => Clients.All.marketIsOpening(new { mh.Key, mh.Value }))
          .ForEach(mh => _marketHoursSet.Add(mh.Key));
      _marketHours
        .Where(mh => (tmTrader.ServerTime.ToUniversalTime().TimeOfDay - mh.Value.TimeOfDay).TotalMinutes > 1)
        .ForEach(mh => _marketHoursSet.Remove(mh.Key));
      #endregion

      #region News
      if (DateTime.Now.Subtract(_newsReadLastDate).TotalMinutes > 1) {
        _newsReadLastDate = DateTime.Now;
        var now = DateTimeOffset.UtcNow;
        var then = now.AddMinutes(60);
        var news = ReadNewsFromDb()
          .Where(n => !_newsDates.Contains(n.Time))
          .Where(n => n.Time > now && n.Time < then)
          .ToArray();
        if (news.Any()) {
          _newsDates.AddRange(news.Select(n => n.Time));
          Clients.All.newsIsComming(news.Select(n => new { n.Time, n.Name }).ToArray());
        }
      }
      #endregion
      return new {
        time = tm0.ServerTime.ToString("HH:mm:ss"),
        prf = IntOrDouble(tmTrader.CurrentGrossInPipTotal, 1),
        otg = IntOrDouble(tmTrader.OpenTradesGross2InPips, 1),
        tps = tm0.TicksPerSecondAverage.Round(1),
        dur = TimeSpan.FromMinutes(tm0.RatesDuration).ToString(@"hh\:mm"),
        hgt = tmTrader.RatesHeightInPips.ToInt() + "/" + tmTrader.BuySellHeightInPips.ToInt(),
        rsdMin = tm0.RatesStDevMinInPips,
        rsdMin2 = tm1 == null ? 0 : tm1.RatesStDevMinInPips,
        S = remoteControl.Value.MasterModel.AccountModel.Equity.Round(0),
        price = new { ask = tm0.CurrentPrice.Ask, bid = tm0.CurrentPrice.Bid },
        tci = GetTradeConditionsInfo(tmTrader),
        wp = tmTrader.WaveHeightPower.Round(1)
        //closed = trader.Value.ClosedTrades.OrderByDescending(t=>t.TimeClose).Take(3).Select(t => new { })
      };
    }

    #region TradeDirectionTriggers
    public string[] ReadTradeDirectionTriggers(string pair) {
      return UseTradingMacro(pair, tm => tm.TradeDirectionTriggersAllInfo((tc, name) => name).ToArray(), false);
    }
    public string[] GetTradeDirectionTriggers(string pair) {
      return UseTradingMacro(pair, tm => tm.TradeDirectionTriggersInfo((tc, name) => name).ToArray(), false);
    }
    public void SetTradeDirectionTriggers(string pair, string[] names) {
      UseTradingMacro(pair, tm => tm.TradeDirectionTriggersSet(names), true);
    }
    #endregion

    #region TradeConditions
    public string[] ReadTradingConditions(string pair) {
      return UseTradingMacro(pair, tm => tm.TradeConditionsAllInfo((tc, p, name) => name).ToArray(), false);
    }
    public string[] GetTradingConditions(string pair) {
      return UseTradingMacro(pair, tm => tm.TradeConditionsInfo((tc, p, name) => name).ToArray(), false);
    }
    public void SetTradingConditions(string pair, string[] names) {
      UseTradingMacro(pair, tm => tm.TradeConditionsSet(names), true);
    }
    public Dictionary<string, Dictionary<string, bool>> GetTradingConditionsInfo(string pair) {
      return UseTradingMacro(pair, tm => GetTradeConditionsInfo(tm), false);
    }

    private static Dictionary<string, Dictionary<string, bool>> GetTradeConditionsInfo(TradingMacro tm) {
      return tm.TradeConditionsInfo((d, p, t, c) => new { c, t, d = d() })
        .GroupBy(x => x.t)
        .Select(g => new { g.Key, d = g.ToDictionary(x => x.c, x => x.d.Any()) })
        .ToDictionary(x => x.Key + "", x => x.d);
    }
    #endregion

    #region TradeOpenActions
    public string[] ReadTradeOpenActions(string pair) {
      return UseTradingMacro(pair, tm => tm.TradeOpenActionsAllInfo((tc, name) => name).ToArray(), false);
    }
    public string[] GetTradeOpenActions(string pair) {
      return UseTradingMacro(pair, tm => tm.TradeOpenActionsInfo((tc, name) => name).ToArray(), false);
    }
    public void SetTradeOpenActions(string pair, string[] names) {
      UseTradingMacro(pair, tm => tm.TradeOpenActionsSet(names), true);
    }
    #endregion

    public ExpandoObject GetWwwInfo(string pair, int chartNum) {
      return UseTradingMacro(pair, chartNum, tm => tm.WwwInfo(), false);
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
        GetTradingMacro(pair).ForEach(tm => tm.CloseTrades("SignalR"));
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
      }
    }
    public void MoveTradeLevel(string pair, bool isBuy, double pips) {
      try {
        GetTradingMacro(pair).ForEach(tm => tm.MoveBuySellLeve(isBuy, pips));
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
      }
    }
    public void ManualToggle(string pair) {
      try {
        GetTradingMacro(pair).ForEach(tm => tm.ResetSuppResesInManual());
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
      }
    }
    public void ToggleStartDate(string pair, int chartNumber) {
      UseTradingMacro(pair, chartNumber, tm => tm.ToggleCorridorStartDate(), true);
    }
    public void ToggleIsActive(string pair, int chartNumber) {
      UseTradingMacro(pair, chartNumber, tm => tm.ToggleIsActive(), true);
    }
    public void FlipTradeLevels(string pair) {
      UseTradingMacro(pair, tm => tm.FlipTradeLevels(), true);
    }
    public void WrapTradeInCorridor(string pair) {
      UseTradingMacro(pair, tm => tm.WrapTradeInCorridor(), true);
    }
    public void WrapCurrentPriceInCorridor(string pair) {
      UseTradingMacro(pair, tm => tm.WrapCurrentPriceInCorridor(), true);
    }
    public void Buy(string pair) {
      UseTradingMacro(pair, tm => tm.OpenTrade(true, tm.LotSizeByLossBuy, "web"), true);
    }
    public void Sell(string pair) {
      UseTradingMacro(pair, tm => tm.OpenTrade(false, tm.LotSizeByLossBuy, "web"), true);
    }
    public void SetRsdTreshold(string pair, int chartNum, int pips) {
      UseTradingMacro(pair, chartNum, tm => tm.RatesStDevMinInPips = pips, true);
    }
    public object[] AskRates(int charterWidth, DateTimeOffset startDate, DateTimeOffset endDate, string pair, int chartNum) {
      return UseTradingMacro2(pair, chartNum, tm => tm.IsActive
        , tm => remoteControl.Value.ServeChart(charterWidth, startDate, endDate, tm), false);
    }

    static int _sendChartBufferCounter = 0;
    static SendChartBuffer _sendChartBuffer = SendChartBuffer.Create();
    public void AskRates_(int charterWidth, DateTimeOffset startDate, DateTimeOffset endDate, string pair) {
      UseTradingMacro2(pair, 0, tm => {
        if (tm.IsActive)
          _sendChartBuffer.Push(() => {
            if (_sendChartBufferCounter > 1) {
              throw new Exception(new { _sendChartBufferCounter } + "");
            }
            _sendChartBufferCounter++;
            Clients.Caller.sendChart(pair, 0, remoteControl.Value.ServeChart(charterWidth, startDate, endDate, tm));
            _sendChartBufferCounter--;
          });
      }, false);
    }

    static SendChartBuffer _sendChart2Buffer = SendChartBuffer.Create();
    public object[] AskRates2(int charterWidth, DateTimeOffset startDate, DateTimeOffset endDate, string pair) {
      return UseTradingMacro2(pair, 1, tm => tm.IsActive
        , tm => remoteControl.Value.ServeChart(charterWidth, startDate, endDate, tm), false);
    }
    public void SetPresetTradeLevels(string pair, TradeLevelsPreset presetLevels, object isBuy) {
      bool? b = isBuy == null ? null : (bool?)isBuy;
      UseTradingMacro(pair, tm => tm.SetTradeLevelsPreset(presetLevels, b), true);
    }
    public void SetTradeLevel(string pair, bool isBuy, int level) {
      //var l = level.GetType() == typeof(string) ? (TradeLevelBy)Enum.Parse(typeof(TradeLevelBy), level+"", false) : (TradeLevelBy)level;
      UseTradingMacro(pair, tm => {
        if (isBuy) {
          tm.LevelBuyBy = (TradeLevelBy)level;
        } else {
          tm.LevelSellBy = (TradeLevelBy)level;
        }
      }, true);
    }
    public void SetTradeCloseLevel(string pair, bool isBuy, int level) {
      UseTradingMacro(pair, tm => {
        if (isBuy) {
          tm.LevelBuyCloseBy = (TradeLevelBy)level;
        } else {
          tm.LevelSellCloseBy = (TradeLevelBy)level;
        }
      }, true);
    }
    public object[] ReadNews() {
      return ReadNewsFromDb().ToArray(en => new { en.Time, en.Name, en.Level, en.Country });
    }

    private static DB.Event__News[] ReadNewsFromDb() {
      var date = DateTimeOffset.UtcNow.AddMinutes(-60);
      var countries = new[] { "USD", "US", "GBP", "GB", "EUR", "EMU", "JPY", "JP" };
      return GlobalStorage.UseForexContext(c => c.Event__News.Where(en => en.Time > date && countries.Contains(en.Country)).ToArray());
    }
    public Dictionary<string, int> ReadEnum(string enumName) {
      return typeof(TradingMacro).Assembly.GetTypes()
        .Where(t => t.Name.ToLower() == enumName)
        .SelectMany(et => Enum.GetNames(et), (et, e) => new { name = e, value = (int)Enum.Parse(et, e, true) })
        .ToDictionary(t => t.name, t => t.value);
    }
    public Dictionary<string, int> ReadTradeLevelBys() {
      return Enum.GetValues(typeof(TradeLevelBy)).Cast<TradeLevelBy>().ToDictionary(t => t.ToString(), t => (int)t);
    }
    #region TradeSettings
    public ExpandoObject SaveTradeSettings(string pair, int chartNum, ExpandoObject ts) {
      return UseTradingMacro(pair, chartNum, tm => {
        var props = (IDictionary<string, object>)ts;
        props.ForEach(kv => {
          tm.SetProperty(kv.Key, kv.Value, p => p.GetSetMethod() != null);
        });
        return ReadTradeSettings(pair, chartNum);
      }, true);
    }
    public ExpandoObject ReadTradeSettings(string pair, int chartNum) {
      return UseTradingMacro(pair, chartNum, tm => {
        var e = (IDictionary<string, object>)new ExpandoObject();
        Func<object, object> convert = o => o != null && o.GetType().IsEnum ? o + "" : o;
        tm.GetPropertiesByAttibute<WwwSettingAttribute>(_ => true)
          .Select(x => new { x.Item1.Group, p = x.Item2 })
          .OrderBy(x => x.p.Name)
          .OrderBy(x => x.Group)
          .ForEach(x => e.Add(x.p.Name, new { v = convert(x.p.GetValue(tm)), g = x.Group }));
        return e as ExpandoObject;
      }, false);
    }
    #endregion
    static string MakePair(string pair) { return pair.Substring(0, 3) + "/" + pair.Substring(3, 3); }
    public Trade[] ReadClosedTrades(string pair) {
      try {
        return new[] { new { p = MakePair(pair), rc = remoteControl.Value } }.SelectMany(x =>
          x.rc.GetClosedTrades(x.p).Concat(x.rc.TradesManager.GetTrades(x.p)))
          .ToArray();
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }
    public object GetWaveRanges(string pair, int chartNum) {
      var value = MonoidsCore.ToFunc(0.0, false, (v, mx) => new { v, mx });
      var wrs = UseTradingMacro(pair, chartNum, tm => true, false)
        .SelectMany(tm => tm.WaveRangesWithTail, (tm, wr) => new { inPips = new Func<double, double>(d => tm.InPips(d)), wr, rs = tm.WaveRanges })
        .Select((x, i) => new {
          i,
          ElliotIndex = value((double)x.wr.ElliotIndex, false),
          Angle = value(x.wr.Angle.Round(0), x.wr.Index(x.rs, wr => wr.Angle.Abs()) == 0),//.ToString("###0.0"),
          Height = value(x.wr.Height.Round(0), x.wr.Index(x.rs, wr => wr.Height) == 0),//.ToString("###0.0"),
          StDev = value(x.wr.StDev.Round(1), x.wr.Index(x.rs, wr => wr.StDev) == 0),//.ToString("#0.00"),
          Distance = value(x.wr.Distance.Round(0), x.wr.Index(x.rs, wr => wr.Distance) == 0),
          DistanceByRegression = value(x.wr.DistanceByRegression.Round(0), x.wr.Index(x.rs, wr => wr.DistanceByRegression) == 0),
          WorkByHeight = value(x.wr.WorkByHeight.Round(0), x.wr.Index(x.rs, wr => wr.WorkByHeight) == 0),
          UID = value(x.wr.UID.Round(1), x.wr.Index(x.rs, wr => -wr.UID) == 0),
          x.wr.IsTail,
          IsStats = false
        })
        .ToList();
      var wrStats = UseTradingMacro(pair, chartNum, tm => true, false)
        .Select(tm => new { wrs = new[] { tm.WaveRangeAvg, tm.WaveRangeSum }, inPips = new Func<double, double>(d => tm.InPips(d)) })
        .SelectMany(x => x.wrs, (x, wr) => new {
          i = 0,
          ElliotIndex = value(0, false),
          Angle = value(wr.Angle.Round(0), false),
          Height = value(wr.Height.Round(0), false),
          StDev = value(wr.StDev.Round(1), false),
          Distance = value(wr.Distance.Round(0), false),
          DistanceByRegression = value(wr.DistanceByRegression.Round(0), false),
          WorkByHeight = value(wr.WorkByHeight.Round(0), false),
          UID = value(wr.UID.Round(1), false),
          IsTail = false,
          IsStats = true
        });
      #region Not Used
      Func<object, double> getProp = (o) =>
        o.GetType()
        .GetProperties()
        .Where(p => p.Name == "v")
        .Select(p => new Func<double>(() => (double)p.GetValue(o)))
        .DefaultIfEmpty(() => (double)o)
        .First()();
      var wra = wrs.Take(0).Select(wr0 => wr0.GetType()
        .GetProperties()
        .ToDictionary(p => p.Name, p => (object)value(
          wrs
          .Select(wr => getProp(p.GetValue(wr)).Abs())
          .DefaultIfEmpty(0)
          .ToArray()
          .AverageInRange(1, -1)
          .Average(),
          false
          )))
          .Do(wr => wr.Add("IsStats", true))
          .ToArray();
      #endregion
      //var wrStd = wrs.Take(1).Select(wr0 => wr0.GetType()
      //  .GetProperties()
      //  .ToDictionary(p => p.Name, p => {
      //    var dbls = wrs.Select(wr => getProp(p.GetValue(wr)).Abs()).ToArray();
      //    return value(dbls.StandardDeviation() / dbls.Height(), false);
      //  }));
      return wrs.Cast<object>()
        //.Concat(wra.Cast<object>())
        .Concat(wrStats.Cast<object>())
        /*.Concat(wrStd.Cast<object>())*/.ToArray();
    }
    double IntOrDouble(double d, double max = 10) {
      return d.Abs() > max ? d.ToInt() : d.Round(1);
    }
    public void RefreshOrders() {
      remoteControl.Value.TradesManager.RefreshOrders();
    }
    public void SetTradeCount(string pair, int tradeCount) {
      GetTradingMacro(pair, tm => tm.SetTradeCount(tradeCount));
    }
    public void StopTrades(string pair) { SetCanTrade(pair, false, null); }
    public void StartTrades(string pair, bool isBuy) { SetCanTrade(pair, true, isBuy); }
    void SetCanTrade(string pair, bool canTrade, bool? isBuy) {
      GetTradingMacro(pair).ForEach(tm => tm.SetCanTrade(canTrade, isBuy));
    }
    private void GetTradingMacro(string pair, Action<TradingMacro> action) {
      GetTradingMacro(pair)
        .ForEach(action);
    }
    private IEnumerable<TradingMacro> GetTradingMacro(string pair, int chartNum = 0) {
      return remoteControl.Value.YieldNotNull()
        .SelectMany(rc => rc.TradingMacrosCopy)
        .Skip(chartNum)
        .Take(1)
        .Where(tm2 => tm2.IsActive)
        .Where(t => t.PairPlain == pair);
    }
    IEnumerable<TradingMacro> UseTradingMacro(string pair, Func<TradingMacro, bool> predicate, bool testTraderAccess) {
      return UseTradingMacro(pair, 0, predicate, testTraderAccess);
    }
    IEnumerable<TradingMacro> UseTradingMacro(string pair, int chartNum, Func<TradingMacro, bool> predicate, bool testTraderAccess) {
      try {
        if (testTraderAccess) TestUserAsTrader();
        return GetTradingMacro(pair, chartNum).Where(predicate).Take(1);
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }
    T UseTradingMacro<T>(string pair, Func<TradingMacro, T> func, bool testTraderAccess) {
      return UseTradingMacro(pair, 0, func, testTraderAccess);
    }
    void UseTradingMacro(string pair, Action<TradingMacro> action, bool testTraderAccess) {
      UseTradingMacro(pair, 0, action, testTraderAccess);
    }
    void UseTradingMacro(string pair, int chartNum, Action<TradingMacro> action, bool testTraderAccess) {
      try {
        if (testTraderAccess) TestUserAsTrader();
        GetTradingMacro(pair, chartNum).ForEach(action);
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }
    T UseTradingMacro<T>(string pair, int chartNum, Func<TradingMacro, T> func, bool testTraderAccess) {
      try {
        if (testTraderAccess) TestUserAsTrader();
        return GetTradingMacro(pair, chartNum).Select(func).DefaultIfEmpty(default(T)).First();
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }
    T[] UseTradingMacro2<T>(string pair, int chartNum, Func<TradingMacro, bool> where, Func<TradingMacro, T> func, bool testTraderAccess) {
      try {
        if (testTraderAccess) TestUserAsTrader();
        return GetTradingMacro(pair, chartNum).Where(where).Select(func).ToArray();
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }
    void UseTradingMacro2(string pair, int chartNum, Action<TradingMacro> func, bool testTraderAccess) {
      try {
        if (testTraderAccess) TestUserAsTrader();
        GetTradingMacro(pair, chartNum).ForEach(func);
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }

  }
}
