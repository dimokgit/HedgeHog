﻿using HedgeHog.Alice.Store;
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
using System.Reflection;
using Westwind.Web.WebApi;
using HedgeHog.Bars;
using Microsoft.Owin.Security;

namespace HedgeHog.Alice.Client {
  public class StartUp {
    public string Pair { get { return "usdjpy"; } }
    List<string> Pairs = new List<string>() { "usdjpy", "eurusd" };
    double IntOrDouble(double d, double max = 10) {
      return d.Abs() > max ? d.ToInt() : d.Round(1);
    }
    public void Configuration(IAppBuilder app) {
      Func<HttpListener> httpListener = () => (HttpListener)app.Properties["System.Net.HttpListener"];
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
      Func<IHubContext> myHub = () => GlobalHost.ConnectionManager.GetHubContext<MyHub>();
      var remoteControl = App.container.GetExport<RemoteControlModel>();
      IDisposable priceChanged = null;
      IDisposable tradesChanged = null;
      IDisposable lasrWwwErrorChanged = null;
      {
        var trader = App.container.GetExportedValue<TraderModel>();

        NewThreadScheduler.Default.Schedule(TimeSpan.FromSeconds(1), () => {
          priceChanged = trader.PriceChanged
            .Select(x => x.EventArgs.Pair.Replace("/", "").ToLower())
            .Where(pair => Pairs.Contains(pair))
            .Subscribe(pair => {
              try {
                myHub().Clients.All.priceChanged(pair);
              } catch(Exception exc) {
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
              } catch(Exception exc) {
                GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
              }
            });
          lasrWwwErrorChanged = remoteControl.Value.ReplayArguments.LastWwwErrorObservable
          .Where(le => !string.IsNullOrWhiteSpace(le))
          .Subscribe(le => myHub().Clients.All.lastWwwErrorChanged(le));
        });
      }
      app.UseCors(CorsOptions.AllowAll);
      Action<RemoteControlModel> setAuthScheme = rc => {
        if(!rc.IsInVirtualTrading) {
          httpListener().AuthenticationSchemes = AuthenticationSchemes.IntegratedWindowsAuthentication;
        }
      };
      app.Use((context, next) => {
        try {
          if(context == null)
            App.SetSignalRSubjectSubject(() => {
              setAuthScheme?.Invoke(remoteControl.Value);
              setAuthScheme = null;
              var tm = remoteControl.Value.TradingMacrosCopy.FirstOrDefault(t => t.PairPlain == Pair);
              if(tm != null) {
                try {
                  GlobalHost.ConnectionManager.GetHubContext<MyHub>()
                    .Clients.All.addMessage();
                } catch(Exception exc) {
                  GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
                }
              }
            });

          if(context.Request.Path.Value.ToLower().StartsWith("/hello")) {
            var key = context.Request.Path.Value.ToLower().Split('/').Last();
            var scheme = BasicAuthenticationFilter.AuthenticationSchemes = key == "hello"
            ? AuthenticationSchemes.IntegratedWindowsAuthentication
            : key == "off"
            ? AuthenticationSchemes.Anonymous
            : AuthenticationSchemes.Basic;
            httpListener().AuthenticationSchemes = scheme;
            return context.Response.WriteAsync(new { httpListener().AuthenticationSchemes } + "");
          }
          if(context.Request.Path.Value.ToLower().StartsWith("/logon")) {
            if(context.Authentication.User != null) {
              context.Response.Redirect("/who");
              return context.Response.WriteAsync(UserToString(httpListener, context.Authentication.User));
            }
            if(httpListener().AuthenticationSchemes == AuthenticationSchemes.Anonymous)
              return context.Response.WriteAsync(new { AuthenticationSchemes.Anonymous } + "");
            var host = context.Request.Uri.DnsSafeHost;
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.Headers.Add("WWW-Authenticate", new[] { string.Format("Basic realm=\"{0}\"", host) });
            return Task.FromResult("");
          }
          if(context.Request.Path.Value.ToLower().StartsWith("/logoff")) {
            if(httpListener().AuthenticationSchemes == AuthenticationSchemes.Anonymous) {
              context.Response.Redirect("/who");
              return context.Response.WriteAsync(new { AuthenticationSchemes.Anonymous } + "");
            }
            var host = context.Request.Uri.DnsSafeHost;
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.Headers.Add("WWW-Authenticate", new[] { string.Format("Basic realm=\"{0}\"", host) });
            return Task.FromResult("");
          }
          if(context.Request.Path.Value.ToLower().StartsWith("/who")) {
            var user = context.Authentication.User;
            return context.Response.WriteAsync(user == null
              ? "Anonymous"
              : UserToString(httpListener, user));
          }
          var path = context.Request.Path.Value.ToLower().Substring(1);
          var rc = remoteControl.Value;
          {
            var tm = rc.TradingMacrosCopy.FirstOrDefault(t => t.PairPlain == path);
            if(tm != null) {
              context.Response.ContentType = "image/png";
              return context.Response.WriteAsync(rc.GetCharter(tm).GetPng());
            }
          }
          {
            var tm = rc.TradingMacrosCopy.Skip(1).FirstOrDefault(t => t.PairPlain == path.Replace("2", ""));
            if(tm != null) {
              context.Response.ContentType = "image/png";
              return context.Response.WriteAsync(rc.GetCharter(tm).GetPng());
            }
          }
        } catch(Exception exc) {
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        }
        return next();
      });
      app.Map("/login2", map => {
        map.Run(ctx => {
          if(ctx.Authentication.User == null ||
          !ctx.Authentication.User.Identity.IsAuthenticated) {
            var authenticationProperties = new AuthenticationProperties();
            ctx.Authentication.Challenge(authenticationProperties, new[] { AuthenticationSchemes.Negotiate + "" });
          }
          return Task.FromResult("");
        });
      });
      //app.Use((context, next) => {
      //  return new GZipMiddleware(d => next()).Invoke(context.Environment);
      //});
      // SignalR
      try {
        //GlobalHost.HubPipeline.AddModule(;
        var hubConfiguration = new HubConfiguration();
        hubConfiguration.EnableDetailedErrors = true;
        app.MapSignalR(hubConfiguration);
      } catch(InvalidOperationException exc) {
        if(!exc.Message.StartsWith("Counter"))
          throw;
      }
    }

    private static string UserToString(Func<HttpListener> httpListener, System.Security.Claims.ClaimsPrincipal user) {
      return new { User = user.Identity.Name, user.Identity.AuthenticationType, httpListener().AuthenticationSchemes, IsTrader = user.IsInRole("Traders") }.ToJson();
    }
  }
  [BasicAuthenticationFilter]
  public class MyHub : Hub {
    class SendChartBuffer : AsyncBuffer<SendChartBuffer, Action> {
      protected override Action PushImpl(Action action) {
        return action;
      }
    }
    Lazy<RemoteControlModel> remoteControl;
    Lazy<TraderModel> trader;
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
      try {
        remoteControl = App.container.GetExport<RemoteControlModel>();
        trader = App.container.GetExport<TraderModel>();
        App.WwwMessageWarning.Do(wm => {
          Clients.All.message(wm);
        });
        App.WwwMessageWarning.Clear();
      } catch(ObjectDisposedException) { }
    }

    static DateTime _newsReadLastDate = DateTime.MinValue;
    static List<DateTimeOffset> _newsDates = new List<DateTimeOffset>();
    public object AskChangedPrice(string pair) {
      var tm0 = UseTradingMacro(pair, tm => tm);
      if(tm0 == null)
        return new { };
      var tm1 = UseTradingMacro(pair, 1, tm => tm);
      var tmTrader = tm0.TradingMacroTrader().Single();
      var tmTrender = tm0.TradingMacroTrender().Last();
      var isVertual = tmTrader.IsInVirtualTrading;

      #region marketHours
      if(!tmTrader.IsInVirtualTrading)
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
      if(DateTime.Now.Subtract(_newsReadLastDate).TotalMinutes > 1) {
        _newsReadLastDate = DateTime.Now;
        var now = DateTimeOffset.UtcNow;
        var then = now.AddMinutes(60);
        var news = ReadNewsFromDb()
          .Where(n => !_newsDates.Contains(n.Time))
          .Where(n => n.Time > now && n.Time < then)
          .ToArray();
        if(news.Any()) {
          _newsDates.AddRange(news.Select(n => n.Time));
          Clients.All.newsIsComming(news.Select(n => new { n.Time, n.Name }).ToArray());
        }
      }
      #endregion
      var timeFormat = (isVertual ? "MMM/d " : "") + "HH:mm:ss";
      var digits = tmTrader.Digits();
      Func<TradingMacro, string> timeFrame = tm => tm.BarPeriod > Bars.BarsPeriodType.t1
      ? (tm.RatesArray.Count * tm.BarPeriodInt).FromMinutes().TotalDays.Round(1) + ""
      : TimeSpan.FromMinutes(tm0.RatesDuration).ToString(@"h\:mm");
      return new {
        time = tm0.ServerTime.ToString(timeFormat),
        prf = IntOrDouble(tmTrader.CurrentGrossInPipTotal, 3),
        otg = IntOrDouble(tmTrader.OpenTradesGross2InPips, 3),
        tps = tm0.TicksPerSecondAverage.Round(1),
        dur = timeFrame(tmTrader)
          + (tm1 != null ? "," + timeFrame(tm1) : ""),// + "|" + TimeSpan.FromMinutes(tm1.RatesDuration).ToString(@"h\:mm"),
        hgt = string.Join("/", new[] {
          tmTrender.RatesHeightInPips.ToInt()+"",
          tmTrader.BuySellHeightInPips.ToInt()+"",
          tmTrender.TrendLinesBlueTrends.Angle.Abs().Round(1)+"°"
        }),
        rsdMin = tm0.RatesStDevMinInPips,
        rsdMin2 = tm1 == null ? 0 : tm1.RatesStDevMinInPips,
        S = remoteControl.Value.MasterModel.AccountModel.Equity.Round(0),
        price = tmTrader.CurrentPrice.YieldNotNull().Select(cp => new { ask = cp.Ask, bid = cp.Bid }).DefaultIfEmpty(new object()).Single(),
        tci = GetTradeConditionsInfo(tmTrader),
        wp = tmTrader.WaveHeightPower.Round(1),
        ip = remoteControl.Value.ReplayArguments.InPause ? 1 : 0,
        com = new { b = tmTrader.CenterOfMassBuy.Round(digits), s = tmTrader.CenterOfMassSell.Round(digits) },
        com2 = new { b = tmTrader.CenterOfMassBuy2.Round(digits), s = tmTrader.CenterOfMassSell2.Round(digits) },
        com3 = new { b = tmTrader.CenterOfMassBuy3.Round(digits), s = tmTrader.CenterOfMassSell3.Round(digits) },
        tpls = tmTrader.GetTradeLevelsPreset().Select(e => e + "").ToArray(),
        tts = HasMinMaxTradeLevels(tmTrader) ? tmTrender.TradeTrends : "",
        tti = GetTradeTrendIndexImpl(tmTrader, tmTrender)
        //closed = trader.Value.ClosedTrades.OrderByDescending(t=>t.TimeClose).Take(3).Select(t => new { })
      };
    }

    private static bool HasMinMaxTradeLevels(TradingMacro tmTrader) {
      return (from tl in new[] { TradeLevelBy.PriceMax, TradeLevelBy.PriceMin }
              join bs in new[] { tmTrader.LevelBuyBy, tmTrader.LevelSellBy } on tl equals bs
              select bs).Count() == 2;
    }

    public bool IsInVirtual() {
      return remoteControl.Value.IsInVirtualTrading;
    }
    [BasicAuthenticationFilter]
    public bool TogglePause(string pair, int chartNum) {
      return UseTradingMacro(pair, chartNum, tm => remoteControl.Value.IsInVirtualTrading
       ? remoteControl.Value.ToggleReplayPause()
       : tm.ToggleIsActive()
      );
    }
    public string ReadTitleRoot() { return trader.Value.TitleRoot; }

    #region TradeDirectionTriggers
    public string[] ReadTradeDirectionTriggers(string pair) {
      return UseTradingMacro(pair, tm => tm.TradeDirectionTriggersAllInfo((tc, name) => name).ToArray());
    }
    public string[] GetTradeDirectionTriggers(string pair) {
      return UseTradingMacro(pair, tm => tm.TradeDirectionTriggersInfo((tc, name) => name).ToArray());
    }
    [BasicAuthenticationFilter]
    public void SetTradeDirectionTriggers(string pair, string[] names) {
      UseTradingMacro(pair, tm => tm.TradeDirectionTriggersSet(names));
    }
    #endregion

    #region TradeConditions
    public string[] ReadTradingConditions(string pair) {
      return UseTradingMacro(pair, tm => tm.TradeConditionsAllInfo((tc, p, name) => name).ToArray());
    }
    public string[] GetTradingConditions(string pair) {
      return UseTradingMacro(pair, tm => tm.TradeConditionsInfo((tc, p, name) => name).ToArray());
    }
    [BasicAuthenticationFilter]
    public void SetTradingConditions(string pair, string[] names) {
      UseTradingMacro(pair, tm => tm.TradeConditionsSet(names));
    }
    public Dictionary<string, Dictionary<string, bool>> GetTradingConditionsInfo(string pair) {
      return UseTradingMacro(pair, tm => GetTradeConditionsInfo(tm));
    }

    private static Dictionary<string, Dictionary<string, bool>> GetTradeConditionsInfo(TradingMacro tm) {
      return tm.TradeConditionsInfo((d, p, t, c) => new { c, t, d = d() })
        .GroupBy(x => x.t)
        .Select(g => new { g.Key, d = g.ToDictionary(x => x.c, x => x.d.HasAny()) })
        .ToDictionary(x => x.Key + "", x => x.d);
    }
    #endregion

    #region TradeOpenActions
    public string[] ReadTradeOpenActions(string pair) {
      return UseTradingMacro(pair, tm => tm.TradeOpenActionsAllInfo((tc, name) => name).ToArray());
    }
    public string[] GetTradeOpenActions(string pair) {
      return UseTradingMacro(pair, tm => tm.TradeOpenActionsInfo((tc, name) => name).ToArray());
    }
    [BasicAuthenticationFilter]
    public void SetTradeOpenActions(string pair, string[] names) {
      UseTradingMacro(pair, tm => tm.TradeOpenActionsSet(names));
    }
    #endregion

    #region Strategies
    [BasicAuthenticationFilter]
    public void ClearStrategy(string pair) {
      UseTradingMacro(pair, tm => tm.CleanStrategyParameters());
    }
    public async Task<object[]> ReadStrategies(string pair) {
      return await UseTradingMacro(pair
        , async tm => (await RemoteControlModel.ReadStrategies(tm, (nick, name, content, uri, diff)
          => new { nick, name, uri, diff, isActive = diff.IsEmpty() })).ToArray()
        );
    }
    [BasicAuthenticationFilter]
    public void SaveStrategy(string pair, string nick) {
      if(string.IsNullOrWhiteSpace(nick))
        throw new ArgumentException("Is empty", "nick");
      UseTradingMacro(pair, 0, tm => RemoteControlModel.SaveStrategy(tm, nick));
    }
    public async Task RemoveStrategy(string name, bool permanent) {
      await RemoteControlModel.RemoveStrategy(name, permanent);
    }
    [BasicAuthenticationFilter]
    public async Task UpdateStrategy(string pair, string name) {
      await UseTradingMacro(pair, 0, async tm => {
        tm.IsTradingActive = false;
        await RemoteControlModel.LoadStrategy(tm, name);
      });
    }
    [BasicAuthenticationFilter]
    public async Task LoadStrategy(string pair, string strategyPath) {
      await UseTradingMacro(pair, 0, async tm => {
        tm.IsTradingActive = false;
        await RemoteControlModel.LoadStrategy(tm, strategyPath);
      });
    }
    #endregion

    #region ReplayArguments
    public object ReadReplayArguments(string pair) {
      var ra = UseTradingMacro(pair, tm => new {
        remoteControl.Value.ReplayArguments.DateStart,
        isReplayOn = tm.IsInPlayback,
        remoteControl.Value.ReplayArguments.LastWwwError
      });
      remoteControl.Value.ReplayArguments.LastWwwError = "";
      return ra;
    }
    public object StartReplay(string pair, string startWhen) {
      TimeSpan ts;
      DateTime dateStart = TimeSpan.TryParse(startWhen, out ts)
        ? DateTime.Now.Subtract(ts)
        : DateTime.Parse(startWhen);
      remoteControl.Value.ReplayArguments.DateStart = dateStart;
      remoteControl.Value.ReplayArguments.IsWww = true;
      UseTradingMacro(pair, tm => remoteControl.Value.StartReplayCommand.Execute(tm));
      return ReadReplayArguments(pair);
    }
    public object StopReplay(string pair) {
      remoteControl.Value.ReplayArguments.MustStop = true;
      return ReadReplayArguments(pair);
    }
    #endregion
    public object GetWwwInfo(string pair, int chartNum) {
      return UseTradingMacro(pair, chartNum, tm => tm.WwwInfo());
    }

    public void Send(string pair, string newInyterval) {
      try {
        App.SignalRInterval = int.Parse(newInyterval);
        App.ResetSignalR();
      } catch(Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
      }
    }
    public void CloseTrades(string pair) {
      try {
        GetTradingMacro(pair).ForEach(tm => tm.CloseTrades("SignalR"));
      } catch(Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
      }
    }
    public void MoveTradeLevel(string pair, bool isBuy, double pips) {
      try {
        GetTradingMacro(pair).ForEach(tm => tm.MoveBuySellLeve(isBuy, pips));
      } catch(Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
      }
    }
    public void SetTradeRate(string pair, bool isBuy, double price) {
      GetTradingMacro(pair).ForEach(tm => tm.SetTradeRate(isBuy, price));
    }
    public void ManualToggle(string pair) {
      try {
        GetTradingMacro(pair).ForEach(tm => tm.ResetSuppResesInManual());
      } catch(Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
      }
    }
    [BasicAuthenticationFilter]
    public void ToggleStartDate(string pair, int chartNumber) {
      UseTradingMacro(pair, chartNumber, tm => tm.ToggleCorridorStartDate());
    }
    [BasicAuthenticationFilter]
    public bool ToggleIsActive(string pair, int chartNumber) {
      return UseTradingMacro(pair, chartNumber, tm => tm.ToggleIsActive());
    }
    [BasicAuthenticationFilter]
    public void FlipTradeLevels(string pair) {
      UseTradingMacro(pair, tm => tm.FlipTradeLevels());
    }
    [BasicAuthenticationFilter]
    public void WrapTradeInCorridor(string pair) {
      UseTradingMacro(pair, tm => tm.WrapTradeInCorridor());
    }
    [BasicAuthenticationFilter]
    public void WrapCurrentPriceInCorridor(string pair, int corridorIndex) {
      UseTradingMacro(pair, tm => tm.WrapCurrentPriceInCorridor(tm.TrendLinesTrendsAll[corridorIndex]));
    }
    [BasicAuthenticationFilter]
    public void Buy(string pair) {
      UseTradingMacro(pair, tm => tm.OpenTrade(true, tm.LotSizeByLossBuy, "web"));
    }
    [BasicAuthenticationFilter]
    public void Sell(string pair) {
      UseTradingMacro(pair, tm => tm.OpenTrade(false, tm.LotSizeByLossBuy, "web"));
    }
    [BasicAuthenticationFilter]
    public void SetRsdTreshold(string pair, int chartNum, int pips) {
      UseTradingMacro(pair, chartNum, tm => tm.RatesStDevMinInPips = pips);
    }
    public object[] AskRates(int charterWidth, DateTimeOffset startDate, DateTimeOffset endDate, string pair, BarsPeriodType chartNum) {
      var a = UseTradingMacro(pair, tm => tm.BarPeriod == chartNum
        , tm => remoteControl.Value.ServeChart(charterWidth, startDate, endDate, tm));
      return a.SelectMany(x => x).ToArray();

      //var a = UseTradingMacro2(pair, chartNum
      //  , tm => Task.Run(() => remoteControl.Value.ServeChart(charterWidth, startDate, endDate, tm)));
      //var b = await Task.WhenAll(a);
      //return b.SelectMany(x => x).ToArray();
    }

    static int _sendChartBufferCounter = 0;
    static SendChartBuffer _sendChartBuffer = SendChartBuffer.Create();
    public void AskRates_(int charterWidth, DateTimeOffset startDate, DateTimeOffset endDate, string pair) {
      UseTradingMacro2(pair, 0, tm => {
        if(tm.IsActive)
          _sendChartBuffer.Push(() => {
            if(_sendChartBufferCounter > 1) {
              throw new Exception(new { _sendChartBufferCounter } + "");
            }
            _sendChartBufferCounter++;
            Clients.Caller.sendChart(pair, 0, remoteControl.Value.ServeChart(charterWidth, startDate, endDate, tm));
            _sendChartBufferCounter--;
          });
      });
    }

    [BasicAuthenticationFilter]
    public void SetPresetTradeLevels(string pair, TradeLevelsPreset presetLevels, object isBuy) {
      bool? b = isBuy == null ? null : (bool?)isBuy;
      UseTradingMacro(pair, tm => tm.IsTrader, tm => tm.SetTradeLevelsPreset(presetLevels, b));
    }
    void SetPresetTradeLevels(TradingMacro tm, TradeLevelsPreset presetLevels, object isBuy) {
      bool? b = isBuy == null ? null : (bool?)isBuy;
      tm.SetTradeLevelsPreset(presetLevels, b);
    }
    public TradeLevelsPreset[] GetPresetTradeLevels(string pair) {
      return UseTradingMacro(pair, tm => tm.GetTradeLevelsPreset().ToArray());
    }
    [BasicAuthenticationFilter]
    public void SetTradeLevel(string pair, bool isBuy, int level) {
      //var l = level.GetType() == typeof(string) ? (TradeLevelBy)Enum.Parse(typeof(TradeLevelBy), level+"") : (TradeLevelBy)level;
      UseTradingMacro(pair, tm => {
        if(isBuy) {
          tm.LevelBuyBy = (TradeLevelBy)level;
        } else {
          tm.LevelSellBy = (TradeLevelBy)level;
        }
      });
    }
    [BasicAuthenticationFilter]
    public void SetTradeTrendIndex(string pair, int index) {
      UseTradingMacro(pair, tm => tm.IsTrender, tm => {
        var trds = tm.TradingMacroTrader();
        var hasMM = trds.Any(trd => HasMinMaxTradeLevels(trd));
        if(!hasMM)
          trds.ForEach(trd => SetPresetTradeLevels(trd, TradeLevelsPreset.MinMax, null));
        var has = tm.TradeTrendsInt.Contains(index);
        tm.TradeTrends = has
        ? hasMM
        ? string.Join(",", tm.TradeTrendsInt.Where(i => i != index))
        : tm.TradeTrends
        : tm.TradeTrends + "," + index;
      });
    }
    [BasicAuthenticationFilter]
    public int[] GetTradeTrendIndex(string pair) {
      return UseTradingMacro(pair, tm => tm.IsTrender, tm => tm.TradingMacroTrader(trd => GetTradeTrendIndexImpl(trd, tm).Single())).TakeLast(1).SelectMany(i => i).ToArray();
    }

    private static int[] GetTradeTrendIndexImpl(TradingMacro tmTrader, TradingMacro tmTrender) {
      return tmTrader.GetTradeLevelsPreset()
            .Where(tpl => tpl == TradeLevelsPreset.MinMax)
            .SelectMany(_ => tmTrender.TradeTrendsInt.Take(1)).ToArray();
    }

    [BasicAuthenticationFilter]
    public void SetTradeCloseLevel(string pair, bool isBuy, int level) {
      UseTradingMacro(pair, tm => {
        if(isBuy) {
          tm.LevelBuyCloseBy = (TradeLevelBy)level;
        } else {
          tm.LevelSellCloseBy = (TradeLevelBy)level;
        }
      });
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
      return
        typeof(TradingMacro).Assembly.GetTypes()
        .Concat(typeof(HedgeHog.Bars.Rate).Assembly.GetTypes())
        .Where(t => t.Name.ToLower() == enumName.ToLower())
        .SelectMany(et => Enum.GetNames(et), (et, e) => new { name = e, value = (int)Enum.Parse(et, e, true) })
        .IfEmpty(() => { throw new Exception(new { enumName, message = "Not Found" } + ""); })
        .ToDictionary(t => t.name, t => t.value);
    }
    public Dictionary<string, int> ReadTradeLevelBys() {
      return Enum.GetValues(typeof(TradeLevelBy)).Cast<TradeLevelBy>().ToDictionary(t => t.ToString(), t => (int)t);
    }
    #region TradeSettings
    [BasicAuthenticationFilter]
    public ExpandoObject SaveTradeSettings(string pair, int chartNum, ExpandoObject ts) {
      return UseTradingMacro(pair, chartNum, tm => {
        var props = (IDictionary<string, object>)ts;
        props.ForEach(kv => {
          tm.SetProperty(kv.Key, kv.Value, p => p.GetSetMethod() != null);
        });
        return ReadTradeSettings(pair, chartNum);
      });
    }
    public ExpandoObject ReadTradeSettings(string pair, int chartNum) {
      return UseTradingMacro(pair, chartNum, tm => {
        var e = (IDictionary<string, object>)new ExpandoObject();
        Func<object, object> convert = o => o != null && o.GetType().IsEnum ? o + "" : o;
        Func<PropertyInfo, string> dn = pi => pi.GetCustomAttributes<DisplayNameAttribute>().Select(a => a.DisplayName).DefaultIfEmpty(pi.Name).Single();
        tm.GetPropertiesByAttibute<WwwSettingAttribute>(_ => true)
          .Where(x => !x.Item1.Group.ToLower().StartsWith("hide"))
          .Select(x => new { x.Item1.Group, p = x.Item2, dn = dn(x.Item2) })
          .OrderBy(x => x.dn)
          .OrderBy(x => x.Group)
          .ForEach(x => e.Add(x.p.Name, new { v = convert(x.p.GetValue(tm)), g = x.Group, x.dn }));
        return e as ExpandoObject;
      });
    }
    #endregion
    static string MakePair(string pair) { return pair.Substring(0, 3) + "/" + pair.Substring(3, 3); }
    public Trade[] ReadClosedTrades(string pair) {
      try {
        return new[] { new { p = MakePair(pair), rc = remoteControl.Value } }.SelectMany(x =>
          x.rc.GetClosedTrades(x.p).Concat(x.rc.TradesManager.GetTrades(x.p)))
          .ToArray();
      } catch(Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }
    public object GetWaveRanges(string pair, int chartNum) {
      var value = MonoidsCore.ToFunc(0.0, false, (v, mx) => new { v, mx });
      var wrStats = UseTradingMacro(tm => true, pair, chartNum)
        .Select(tm => new { wrs = new[] { tm.WaveRangeAvg, tm.WaveRangeSum }, inPips = new Func<double, double>(d => tm.InPips(d)) })
        .SelectMany(x => x.wrs, (x, wr) => new {
          i = 0,
          ElliotIndex = value(0, false),
          Angle = value(wr.Angle, false),
          Minutes = value(wr.TotalMinutes, false),
          PPM = value(wr.PipsPerMinute, false),
          HSD = value(wr.HSDRatio, false),
          StDev = value(wr.StDev.Round(2), false),
          Distance = value(wr.Distance.Round(0), false),
          DistanceCma = value(wr.DistanceCma.Round(0), false),
          DistanceByRegression = value(wr.DistanceByRegression.Round(0), false),
          WorkByHeight = value(wr.WorkByHeight.Round(0), false),
          IsTail = false,
          IsFOk = false,
          IsDcOk = false,
          IsStats = true
        })
        .ToArray();
      var wrs = UseTradingMacro(tm => true, pair, chartNum)
        .SelectMany(tm => tm.WaveRangesWithTail, (tm, wr) => new { inPips = new Func<double, double>(d => tm.InPips(d)), wr, rs = tm.WaveRanges })
        .Select((x, i) => new {
          i,
          ElliotIndex = value((double)x.wr.ElliotIndex, false),
          Angle = value(x.wr.Angle.Round(0), x.wr.Angle.Abs() >= wrStats[0].Angle.v),//.ToString("###0.0"),
          Minutes = value(x.wr.TotalMinutes.ToInt(), x.wr.TotalMinutes >= wrStats[0].Minutes.v),//.ToString("###0.0"),
          PPM = value(x.wr.PipsPerMinute, x.wr.PipsPerMinute >= wrStats[0].PPM.v),//.ToString("###0.0"),
          HSD = value(x.wr.HSDRatio.Round(1), x.wr.HSDRatio >= wrStats[0].HSD.v),//.ToString("###0.0"),
          StDev = value(x.wr.StDev.Round(4), x.wr.StDev < wrStats[0].StDev.v),//.ToString("#0.00"),
          Distance = value(x.wr.Distance.Round(0), x.wr.Distance > wrStats[0].Distance.v),
          DistanceCma = value(x.wr.DistanceCma.Round(0), x.wr.Index(x.rs, wr => wr.DistanceCma) == 0),
          DistanceByRegression = value(x.wr.DistanceByRegression.Round(0), x.wr.Index(x.rs, wr => wr.DistanceByRegression) == 0),
          WorkByHeight = value(x.wr.WorkByHeight.Round(0), x.wr.Index(x.rs, wr => wr.WorkByHeight) == 0),
          x.wr.IsTail,
          IsFOk = x.wr.IsFatnessOk,
          IsDcOk = x.wr.IsDistanceCmaOk,
          IsStats = false
        })
        .ToList();
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
    public object[] GetAccounting(string pair) {
      var row = MonoidsCore.ToFunc("", (object)null, (n, v) => new { n, v });
      var list = new[] { row("", 0) }.Take(0).ToList();
      var rc = remoteControl.Value;
      var am = rc.MasterModel.AccountModel;
      list.Add(row("BalanceOrg", am.OriginalBalance.ToString("c0")));
      list.Add(row("Balance", am.Balance.ToString("c0")));
      list.Add(row("Equity", am.Equity.ToString("c0")));
      UseTradingMacro(pair, tm => {
        var ht = tm.HaveTrades();
        list.Add(row("CurrentGross", am.CurrentGross.Round(2).ToString("c0") +
          (ht ? "/" + (am.ProfitPercent).ToString("p1") : "")
          + "/" + (am.OriginalProfit).ToString("p1")));
        list.Add(row("PipAmount", tm.PipAmount.AutoRound2(2).ToString("c") + "/" + tm.PipAmountPercent.ToString("p2")));
        list.Add(row("PipsToMC", (ht ? tm.MaxPipsToPMC() : am.PipsToMC).ToString("n0")));
        list.Add(row("LotSize", (tm.LotSize / 1000).ToString("n0") + "K/" + tm.LotSizePercent.ToString("p0")));
      });
      return list.ToArray();
    }
    double IntOrDouble(double d, double max = 10) {
      return d.Abs() > max ? d.ToInt() : d.Round(1);
    }
    public void SetTradeCount(string pair, int tradeCount) {
      GetTradingMacro(pair, tm => tm.SetTradeCount(tradeCount));
    }
    public void StopTrades(string pair) { SetCanTradeImpl(pair, false, null); }
    public void StartTrades(string pair, bool isBuy) { SetCanTrade(pair, true, isBuy); }
    void SetCanTradeImpl(string pair, bool canTrade, bool? isBuy) {
      GetTradingMacro(pair).ForEach(tm => tm.SetCanTrade(canTrade, isBuy));
    }
    [BasicAuthenticationFilter]
    public bool[] ToggleCanTrade(string pair, bool isBuy) {
      return UseTradingMacro(pair, tm => tm.ToggleCanTrade(isBuy)).ToArray();
    }
    public void SetCanTrade(string pair, bool canTrade, bool isBuy) {
      GetTradingMacro(pair).ForEach(tm => {
        tm.BuySellLevels.ForEach(sr => sr.InManual = true);
        tm.SetCanTrade(canTrade, isBuy);
      });
    }
    private void GetTradingMacro(string pair, Action<TradingMacro> action) {
      GetTradingMacro(pair)
        .ForEach(action);
    }
    private IEnumerable<TradingMacro> GetTradingMacro(string pair, int chartNum = 0) {
      return GetTradingMacros(pair)
        .Skip(chartNum)
        .Take(1);
      ;
    }

    private IEnumerable<TradingMacro> GetTradingMacros(string pair) {
      return remoteControl.Value.YieldNotNull()
        .SelectMany(rc => rc.TradingMacrosCopy)
        .Where(tm => tm.IsActive && tm.PairPlain == pair)
        .OrderBy(tm => tm.TradingGroup)
        .ThenBy(tm => tm.PairIndex);
    }

    IEnumerable<TradingMacro> UseTradingMacro(Func<TradingMacro, bool> predicate, string pair) {
      return UseTradingMacro(predicate, pair, 0);
    }
    IEnumerable<TradingMacro> UseTradingMacro(Func<TradingMacro, bool> predicate, string pair, int chartNum) {
      try {
        return GetTradingMacro(pair, chartNum).Where(predicate).Take(1);
      } catch(Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        return new TradingMacro[0];
      }
    }
    T UseTradingMacro<T>(string pair, Func<TradingMacro, T> func) {
      return UseTradingMacro(pair, 0, func);
    }
    void UseTradingMacro(string pair, Action<TradingMacro> action) {
      UseTradingMacro(pair, 0, action);
    }
    void UseTradingMacro(string pair, Func<TradingMacro, bool> where, Action<TradingMacro> action) {
      GetTradingMacros(pair)
        .Where(where)
        .ForEach(action);
    }
    IEnumerable<T> UseTradingMacro<T>(string pair, Func<TradingMacro, bool> where, Func<TradingMacro, T> func) {
      return GetTradingMacros(pair)
        .Where(where)
        .Select(func);
    }
    void UseTradingMacro(string pair, int chartNum, Action<TradingMacro> action) {
      try {
        GetTradingMacro(pair, chartNum).ForEach(action);
      } catch(Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }
    T UseTradingMacro<T>(string pair, int chartNum, Func<TradingMacro, T> func) {
      try {
        return GetTradingMacro(pair, chartNum).Select(func).DefaultIfEmpty(default(T)).First();
      } catch(Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }
    T[] UseTradingMacro2<T>(string pair, Func<TradingMacro, bool> where, Func<TradingMacro, T> func) {
      try {
        return GetTradingMacros(pair).Where(where).Select(func).ToArray();
      } catch(Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }
    IEnumerable<T> UseTradingMacro2<T>(string pair, int chartNum, Func<TradingMacro, T> func) {
      try {
        return GetTradingMacros(pair).Skip(chartNum).Select(func);
      } catch(Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }
    void UseTradingMacro2(string pair, int chartNum, Action<TradingMacro> func) {
      try {
        GetTradingMacro(pair, chartNum).ForEach(func);
      } catch(Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }

  }
}
