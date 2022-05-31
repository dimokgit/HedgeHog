using HedgeHog.Alice.Store;
using HedgeHog.Bars;
using HedgeHog.DateTimeZone;
using HedgeHog.Shared;
using IBApi;
using IBApp;
using IBSampleApp.messages;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using Microsoft.Owin.Cors;
using Microsoft.Owin.FileSystems;
using Microsoft.Owin.Security;
using Microsoft.Owin.StaticFiles;
using Owin;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Westwind.Web.WebApi;
using static HedgeHog.Core.JsonExtensions;
using DynamicData;
using TM_HEDGE = System.Collections.Generic.IEnumerable<(HedgeHog.Alice.Store.TradingMacro tm, string Pair, double HV, double HVP
  , (double All, double Up, double Down) TradeRatio
  , (double All, double Up, double Down) TradeRatioM1
  , (double All, double Up, double Down) TradeAmount
  , double MMR
  , (int All, int Up, int Down) Lot
  , (double All, double Up, double Down) Pip
  , bool IsBuy, bool IsPrime, double HVPR, double HVPM1R)>;
//using CURRENT_HEDGES = System.Collections.Generic.List<(IBApi.Contract contract, double ratio, double price, string context)>;
using CURRENT_HEDGES = System.Collections.Generic.List<HedgeHog.Alice.Store.TradingMacro.HedgePosition<IBApi.Contract>>;
using static IBApp.AccountManager;

namespace HedgeHog.Alice.Client {
  public class StartUp {
    public static Func<IHubContext> MyHub = () => GlobalHost.ConnectionManager.GetHubContext<MyHub>();
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
      var remoteControl = App.container.GetExport<RemoteControlModel>();
      IDisposable priceChanged = null;
      IDisposable tradesChanged = null;
      IDisposable lasrWwwErrorChanged = null;
      {
        var trader = App.container.GetExportedValue<TraderModel>();

        NewThreadScheduler.Default.Schedule(TimeSpan.FromSeconds(1), () => {
          if(!trader.IsInVirtualTrading)
            trader.TradesManager.OrderAddedObservable
            .Select(a => a.Order)
            .Merge(trader.TradesManager.OrderRemovedObservable)
            .Subscribe(o => {
              MyHub().Clients.All.priceChanged("");
            });
          priceChanged = trader.PriceChanged
          .Where(p => !Contract.FromCache(p.EventArgs.Price.Pair).Any(c => c.HasOptions))
            .Select(x => x.EventArgs.Price.Pair.Replace("/", "").ToLower())
            .Subscribe(pair => {
              try {
                MyHub().Clients.All.priceChanged(pair);
              } catch(Exception exc) {
                LogMessage.Send(exc);
              }
            });
          tradesChanged =
            trader.TradeAdded.Select(x => x.EventArgs.Trade.Pair.Replace("/", "").ToLower())
            .Merge(
            trader.TradeRemoved.Select(x => TradesManagerStatic.WrapPair(x.EventArgs.MasterTrade.Pair)))
            .Delay(TimeSpan.FromSeconds(1))
            .Subscribe(pair => {
              try {
                MyHub().Clients.All.tradesChanged();
              } catch(Exception exc) {
                LogMessage.Send(exc);
              }
            });
          lasrWwwErrorChanged = remoteControl.Value.ReplayArguments.LastWwwErrorObservable
          .Where(le => !string.IsNullOrWhiteSpace(le))
          .Subscribe(le => CallLastError(le, MyHub()));
        });
      }
      app.UseCors(CorsOptions.AllowAll);
      Action<RemoteControlModel> setAuthScheme = rc => {
        if(!rc.IsInVirtualTrading) {
          httpListener().AuthenticationSchemes = AuthenticationSchemes.IntegratedWindowsAuthentication;
        }
      };
      setAuthScheme?.Invoke(remoteControl.Value);
      app.Use((context, next) => {
        try {
          if(context == null)
            App.SetSignalRSubjectSubject(() => {
              //var tm = remoteControl.Value.TradingMacrosCopy.FirstOrDefault(t => t.PairPlain == Pair);
              try {
                GlobalHost.ConnectionManager.GetHubContext<MyHub>()
                  .Clients.All.addMessage();
              } catch(Exception exc) {
                LogMessage.Send(exc);
              }
            });

          var localPath = context.Request.Path.Value.ToLower();
          //Debug.WriteLine(new { localPath });
          if(localPath == "/signalr/connect")
            context.Response.StatusCode = (int)HttpStatusCode.OK;


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
          LogMessage.Send(exc);
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
        GlobalHost.HubPipeline.AddModule(new MyHubPipelineModule());
        var hubConfiguration = new HubConfiguration();
        hubConfiguration.EnableDetailedErrors = true;
        app.MapSignalR(hubConfiguration);
      } catch(InvalidOperationException exc) {
        if(!exc.Message.StartsWith("Counter"))
          throw;
      }
    }

    public static dynamic CallLastError(string le, IHubContext myHub) => myHub.Clients.All.lastWwwErrorChanged(le);

    private static string UserToString(Func<HttpListener> httpListener, System.Security.Claims.ClaimsPrincipal user) {
      return new { User = user.Identity.Name, user.Identity.AuthenticationType, httpListener().AuthenticationSchemes, IsTrader = user.IsInRole("Traders") }.ToJson();
    }
  }

  public class MyHubPipelineModule :HubPipelineModule {
    protected override void OnIncomingError(ExceptionContext exceptionContext, IHubIncomingInvokerContext invokerContext) {
      MethodDescriptor method = invokerContext.MethodDescriptor;
      var args = string.Join(", ", invokerContext.Args);
      var log = $"{method.Hub.Name}.{method.Name}({args}) threw the following uncaught exception: {exceptionContext.Error}";
      LogMessage.Send(log);

      base.OnIncomingError(exceptionContext, invokerContext);
    }
  }

  [BasicAuthenticationFilter]
  public partial class MyHub :Hub {
    class SendChartBuffer :AsyncBuffer<SendChartBuffer, Action> {
      protected override Action PushImpl(Action action) {
        return action;
      }
    }

    private const string ALL_COMBOS = "ALL COMBOS";
    static Lazy<RemoteControlModel> remoteControl;
    static Lazy<TraderModel> trader;
    static Exception Log { set { LogMessage.Send(value); } }
    //http://forex.timezoneconverter.com/?timezone=GMT&refresh=5
    static Dictionary<string, DateTimeOffset> _marketHours = new Dictionary<string, DateTimeOffset> {
    { "Frankfurt", DateTimeOffset.Parse("6:00 +0:00") } ,
    { "London", DateTimeOffset.Parse("07:00 +0:00") } ,
    { "New York", DateTimeOffset.Parse("12:00 +0:00") } ,
    { "Sydney", DateTimeOffset.Parse("22:00 +0:00") } ,
    { "Tokyo", DateTimeOffset.Parse("23:00 +0:00") }
    };
    static List<string> _marketHoursSet = new List<string>();
    static ActionAsyncBuffer _currentCombo = new ActionAsyncBuffer(1.FromSeconds());
    static MyHub() {
      remoteControl = App.container.GetExport<RemoteControlModel>();
      trader = App.container.GetExport<TraderModel>();
      App.WwwMessageWarning.Do(wm => {
        StartUp.MyHub().Clients.All.message(wm);
      });
      App.WwwMessageWarning.Clear();
      _currentCombo.Error.Subscribe(exc => StartUp.MyHub().Clients.All.lastWwwErrorChanged(exc.Message));
    }
    static private AccountManager GetAccountManager() => (trader?.Value?.TradesManager as IBWraper)?.AccountManager;
    public MyHub() {
      try {
      } catch(ObjectDisposedException) { }
    }

    static DateTime _newsReadLastDate = DateTime.MinValue;
    static List<DateTimeOffset> _newsDates = new List<DateTimeOffset>();

    [BasicAuthenticationFilter]
    public void UpdateTradingRatio(string pair, double value) => UseTraderMacro(pair, tm => tm.TradingRatio = value);
    [BasicAuthenticationFilter]
    public void UpdateHedgeQuantity(string pair, int value) => UseTraderMacro(pair, tm => tm.HedgeQuantity = value);

    public object[] AskChangedPrice(string pair) {
      return (from tmTrader in UseTraderMacro(pair)
              from tmTrender in tmTrader.TradingMacroTrender().TakeLast(1)
              select new { tmTrader, tmTrender }
      ).Select(t => {
        var isVertual = t.tmTrader.IsInVirtualTrading;

        #region marketHours
        if(!t.tmTrader.IsInVirtualTrading)
          _marketHours
            .Where(mh => !_marketHoursSet.Contains(mh.Key))
            .Where(mh => (t.tmTrader.ServerTime.ToUniversalTime().TimeOfDay - mh.Value.TimeOfDay).TotalMinutes.Between(-15, 0))
            .Do(mh => Clients.All.marketIsOpening(new { mh.Key, mh.Value }))
            .ForEach(mh => _marketHoursSet.Add(mh.Key));
        _marketHours
          .Where(mh => (t.tmTrader.ServerTime.ToUniversalTime().TimeOfDay - mh.Value.TimeOfDay).TotalMinutes > 1)
          .ForEach(mh => _marketHoursSet.Remove(mh.Key));
        #endregion

        #region News
        var doNews = false;
        if(doNews && t.tmTrader.DoNews && DateTime.Now.Subtract(_newsReadLastDate).TotalMinutes > 1) {
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
        var digits = t.tmTrader.Digits();
        Func<TradingMacro, string> timeFrame = tm => tm.BarPeriod > Bars.BarsPeriodType.t1
        ? (tm.RatesArray.Count * tm.BarPeriodInt).FromMinutes().TotalDays.Round(1) + ""
        : TimeSpan.FromMinutes(t.tmTrader.RatesDuration).ToString(@"h\:mm");
        var cgp = t.tmTrader.CurrentGrossInPips;
        var otgp = t.tmTrader.OpenTradesGross2InPips;
        var ht = trader.Value?.TradesManager.GetTrades().Any();
        return (object)new {
          time = t.tmTrader.ServerTime.ToString(timeFormat),
          prf = IntOrDouble(t.tmTrader.CurrentGrossInPipTotal, 3),
          otg = IntOrDouble(t.tmTrader.OpenTradesGross2InPips, 3),
          tps = t.tmTrader.TicksPerSecondAverage.Round(1),
          dur = timeFrame(t.tmTrader)
            + t.tmTrader.TradingMacroOther().Select(tm1 => "/" + timeFrame(tm1)).Flatter(""),// + "|" + TimeSpan.FromMinutes(tm1.RatesDuration).ToString(@"h\:mm"),
          hgt = string.Join("/", new[] {
            t.tmTrender.RatesHeightInPips.ToInt()+"",
            t.tmTrader.BuySellHeightInPips.ToInt()+"",
            t.tmTrender.TLBlue.Angle.Abs().Round(1)+"°"
          }),
          S = remoteControl.Value.MasterModel.AccountModel.Equity.Round(0),
          price = t.tmTrader.CurrentPrice.YieldNotNull().Select(cp => new { ask = cp.Ask, bid = cp.Bid }).DefaultIfEmpty(new object()).Single(),
          tci = GetTradeConditionsInfo(t.tmTrader),
          wp = t.tmTrader.WaveHeightPower.Round(1),
          ip = remoteControl.Value.ReplayArguments.InPause ? 1 : 0,
          com = new { b = t.tmTrader.CenterOfMassBuy.Round(digits), s = t.tmTrader.CenterOfMassSell.Round(digits), dates = t.tmTrader.CenterOfMassDates ?? new DateTime[0] },
          com2 = new { b = t.tmTrader.CenterOfMassBuy2.Round(digits), s = t.tmTrader.CenterOfMassSell2.Round(digits), dates = t.tmTrader.CenterOfMass2Dates ?? new DateTime[0] },
          com3 = new { b = t.tmTrader.CenterOfMassBuy3.Round(digits), s = t.tmTrader.CenterOfMassSell3.Round(digits), dates = t.tmTrader.CenterOfMass3Dates ?? new DateTime[0] },
          com4 = new { b = t.tmTrader.CenterOfMassBuy4.Round(digits), s = t.tmTrader.CenterOfMassSell4.Round(digits), dates = t.tmTrader.CenterOfMass4Dates ?? new DateTime[0] },
          bth = t.tmTrader.BeforeHours.Select(tr => new { tr.upDown, tr.dates }).ToArray(),
          bcl = t.tmTrader.AfterHours.Select(tr => new { tr.upDown, tr.dates }).ToArray(),
          //afh2 = new[] { new { dates = t.tmTrader.ServerTime.Date.AddDays(-1).AddHours(16).With(d => new[] { d, d.AddHours(4) }) } },
          afh = GetBackDates(t.tmTrader.ServerTime.Date, 9)
            .Select(date => new { dates = date.AddHours(16).With(d => new[] { d, d.AddHours(4) }) })
            .ToArray(),
          tpls = t.tmTrader.GetTradeLevelsPreset().Select(e => e + "").ToArray(),
          tts = HasMinMaxTradeLevels(t.tmTrader) ? t.tmTrender.TradeTrends : "",
          tti = GetTradeTrendIndexImpl(t.tmTrader, t.tmTrender),
          ht
          //closed = trader.Value.ClosedTrades.OrderByDescending(t=>t.TimeClose).Take(3).Select(t => new { })
        };
      })
      .ToArray();
      DateTime[] GetBackDates(DateTime start, int daysBack) =>
        Enumerable.Range(0, 1000)
        .Where(_ => !start.IsMin())
        .Select(d => start.AddDays(-d))
        .Where(d => !new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }.Contains(d.DayOfWeek))
        .Take(daysBack)
        .ToArray();
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
      return UseTradingMacro2(pair, chartNum, tm => remoteControl.Value.IsInVirtualTrading
       ? remoteControl.Value.ToggleReplayPause()
       : tm.ToggleIsActive()
      ).DefaultIfEmpty().First();
    }
    public string ReadTitleRoot() { return trader.Value.TitleRoot; }

    #region TradeDirectionTriggers
    public string[] ReadTradeDirectionTriggers(string pair) {
      return UseTradingMacro2(pair, 0, tm => tm.TradeDirectionTriggersAllInfo((tc, name) => name)).Concat().ToArray();
    }
    public string[] GetTradeDirectionTriggers(string pair) {
      return UseTradingMacro2(pair, 0, tm => tm.TradeDirectionTriggersInfo((tc, name) => name)).Concat().ToArray();
    }
    [BasicAuthenticationFilter]
    public void SetTradeDirectionTriggers(string pair, string[] names) {
      UseTradingMacro(pair, tm => { tm.TradeDirectionTriggersSet(names); });
    }
    #endregion

    #region MMRs
    public void GetMMRs() {
      trader.Value.TradesManager.FetchMMRs();
    }
    public object[] LoadOffers() {
      return TraderModel.LoadOffers().Select(o => new { pair = o.Pair, mmrBuy = o.MMRLong.Round(3), mmrSell = o.MMRShort.Round(3) }).ToArray();
    }
    public object[] ReadOffers() {
      var equity = remoteControl.Value.MasterModel.AccountModel.Equity;
      return TradesManagerStatic.dbOffers.Select(o => new {
        pair = o.Pair,
        mmrBuy = o.MMRLong.Round(3),
        mmrSell = o.MMRShort.Round(3),
        pipBuy = TradeStats(o.Pair, true).SingleOrDefault(),
        pipSel = TradeStats(o.Pair, false).SingleOrDefault()
      }).ToArray();

      IEnumerable<string> TradeStats(string pair, bool isBuy) =>
        UseTraderMacro(pair, tm =>
        (tm, lot: tm.GetLotsToTrade(equity, TradesManagerStatic.GetMMR(tm.Pair, isBuy), 1)))
        .Select(t => (
          t.lot,
          t.tm,
          pip: TradesManagerStatic.PipAmount(t.tm.Pair, t.lot, (t.tm.CurrentPrice?.Average).GetValueOrDefault(), TradesManagerStatic.GetPointSize(t.tm.Pair))))
        .Select(t => (t.pip, h: t.tm.RatesHeightInPips * t.pip, hv: t.tm.HistoricalVolatility().SingleOrDefault()))
        .Select(t => $"{t.hv.AutoRound2(3)},{t.pip.AutoRound2("$", 3)},{t.h.ToString("c0")}");

    }
    public void UpdateMMRs(string pair, double mmrLong, double mmrShort) {
      pair = pair.ToUpper();
      GlobalStorage.UseForexMongo(c => c.Offer.Where(o => o.Pair == pair).ForEach(o => {
        o.MMRLong = mmrLong;
        o.MMRShort = mmrShort;
      }), true);
    }
    public void SaveOffers() {
      GlobalStorage.UseForexMongo(c =>
      (from o in c.Offer
       join dbo in TradesManagerStatic.dbOffers on o.Pair.ToLower() equals dbo.Pair.ToLower()
       select new { o, dbo }
      ).ForEach(x => {
        x.o.MMRLong = x.dbo.MMRLong;
        x.o.MMRShort = x.dbo.MMRShort;
      }), true);

    }


    bool IsTestTradeMode() => Debugger.IsAttached;
    static bool useComboOrder = true;
    [BasicAuthenticationFilter]
    public async Task<object[]> OpenHedged(string pair, string key, int quantity, bool isBuy, bool rel, bool test) {
      var outMe = MonoidsCore.ToFunc((string order, string errorMsg) => new { order, errorMsg });
      var c = Contract.FromCache(key).SingleOrDefault();
      var positions = quantity * (isBuy ? 1 : -1);
      if(IsInVirtual()) {
        var tm = UseTraderMacro(pair).SingleOrDefault();
        var hp = c.LegsEx(t => t.c.Instrument).Skip(1).SingleOrDefault();
        var hedgeIndex = tm.PairHedges.Select((h, i) => (h, i)).Where(t => t.h == hp).Select(t => t.i).SingleOrDefault();
        var legs = c.Legs().ToList();
        tm.OpenHedgePosition(isBuy, legs[0].r * quantity, legs[1].r * quantity, hedgeIndex);
        return new object[0];
      } else {
        var am = GetAccountManager();
        if(c == null) throw new Exception($"{new { key }} not found");
        if(rel)
          return await (from ots in OpenHedgeRELTrade(am, c, positions, test)
                        from ot in ots
                        select outMe(ot.holder + "", ot.error.errorMsg)
                        ).ToArray();
        if(rel && c.IsHedgeCombo) {
          var contracts = c.LegsEx(t => new { t.c, q = t.leg.Quantity * positions })
            .OrderByDescending(x => x.c.Instrument.ToLower() == pair.ToLower());
          var parent = contracts.First();
          var child = contracts.Skip(1).First();
          return await (
            from ots in am.OpenHedgeOrder(parent.c, child.c, parent.q, child.q)
            from ot in ots
            select outMe(ot.holder + "", ot.error.errorMsg)
                        ).ToArray();
        }
        if(!rel || c.IsOptionsCombo) {
          var oe = new Action<IBApi.Order>(o => o.Transmit = !test);
          var legs = await (from t in c.LegsEx().ToObservable()
                            from ots in am.OpenTradeWithAction(oe, t.contract, t.leg.Quantity * positions)
                            from ot in ots
                            select outMe(ot.holder + "", ot.error.errorMsg)
                           ).ToArray();
          return legs;
        }
        try {
          return await (
            from p in c.ReqPriceSafe(5)
            let price = p.ask.Avg(p.bid)
            let takeProfit = (isBuy ? p.ask : p.bid).With(p2 => TradesManagerStatic.PriceFromProfitAmount(500, quantity, c.ComboMultiplier, p2).Abs(p2))
            let oe = new Action<IBApi.Order>(o => {
              o.Transmit = !test;
              if(rel && !c.IsOptionsCombo) {
                o.AuxPrice = IBApi.Order.OrderPrice(price, c);
                o.OrderType = "REL + MKT";
              }
            })
            from ots in am.OpenTradeWithAction(oe, c, positions, 0, 0, false)
            from ot in ots
            select outMe(ot.holder + "", ot.error.errorMsg)
           ).ToArray();
        } finally {
          Clients.Caller.MustReadStraddles();
        }
      }
    }
    static IObservable<OrderContractHolderWithError[]> OpenHedgeRELTrade
      (AccountManager am, Contract c, int positions, bool isTest) {
      return (
        from price in c.ReqPriceSafe(5).Select(p => p.Price(positions > 0))
        let oe = new Action<IBApi.Order>(o => {
          o.Transmit = !isTest;
          o.LmtPrice = o.AuxPrice = IBApi.Order.OrderPrice(price, c);
          o.OrderType = "LMT";
        })
        from ots in am.OpenTradeWithAction(oe, c, positions)
        from ot in ots
        select ot
        ).ToArray();
    }

    static double DaysTillExpiration2(DateTime expiration) => (expiration.InNewYork().AddHours(16) - DateTime.Now.InNewYork()).TotalDays.Max(1);
    static double DaysTillExpiration(DateTime expiration) => (expiration - DateTime.Now.Date).TotalDays + 1;
    public void SetHedgeCalcType(string pair, string hedgeCalcTypeContext) {
      var m = System.Text.RegularExpressions.Regex.Match(hedgeCalcTypeContext, @"(\D+)(\d*)");
      var type = m.Groups[1] + "";
      int.TryParse(m.Groups[2] + "", out var index);
      var hct = EnumUtils.Parse<TradingMacro.HedgeCalcTypes>(type);
      UseTraderMacro(pair, tm => {
        tm.HedgeCalcType = hct;
        tm.HedgeCalcIndex = index;
      });
    }
    public async Task<object[]> ReadStraddles(string pair
      , int gap, int numOfCombos, int quantity, double? strikeLevel, int expDaysSkip
      , string optionTypeMap, DateTime hedgeDate, string rollCombo
      , string[] selectedCombos
      //, string[] bookPositions
      , ExpandoObject context
      ) {
      var task = (await UseTraderMacro(pair, async tm => {
        const string HID_BYTV = "ByTV";
        const string HID_BYHV = "ByHV";
        const string HID_BYPOS = "ByPos";
        var useNaked = numOfCombos >= 0;
        numOfCombos = numOfCombos.Abs();
        var contextDict = (IDictionary<string, object>)context;
        var selectedHedge = tm.HedgeCalcType;// (string)(contextDict["selectedHedge"] ?? "");
        int.TryParse(Convert.ToString(contextDict["hedgeQuantity"] ?? "1"), out var hedgeQuantity);
        if(!tm.IsInVirtualTrading) {
          int expirationDaysSkip = TradesManagerStatic.ExpirationDaysSkip(expDaysSkip);
          var am = GetAccountManager();
          string CacheKey(Contract c) => c.IsFuture ? c.LocalSymbol : c.Symbol;
          var uc = contextDict["optionsUnder"]?.ToString().IfEmpty(pair);
          var unders = await (from cd in uc.ReqContractDetailsCached()
                              from price in cd.Contract.ReqPriceSafe()
                              select new { cd.Contract, price }
                              ).ToArray();
          Contract[] underContracts = unders.ToArray(x => x.Contract);
          if(am != null) {
            List<object> bookPositions = contextDict.ContainsKey("bookPositions")
            ? (List<object>)contextDict["bookPositions"]
            : new object[0].ToList();
            var bookStraddle = (from bp in (bookPositions?.ToArray(o => o.ToString()) ?? new string[0]).ToObservable()
                                from c in bp.ReqContractDetailsCached()
                                select c.Contract
                                ).ToArray()
                                .Where(cp => cp.Length == 2)
                                .SelectMany(cp => am.StraddleFromContracts(cp));
            Action rollOvers = () => {
              var show = !rollCombo.IsNullOrWhiteSpace() && optionTypeMap == "R";
              if(!show) return;
              int.TryParse((string)(contextDict["currentProfit"] ?? "0"), out var currentProfit);
              (from rollCd in rollCombo.ReqContractDetailsCached()
               let under = rollCd.UnderSymbol
               from cr in am.CurrentRollOverByUnder(under, quantity, numOfCombos, expDaysSkip.Max(1), currentProfit)
               select cr
               )
              .ToArray()
               //.Where(a => a.Length > 0)
               .Subscribe(_ => {
                 base.Clients.Caller.rollOvers(_.OrderByDescending(t => t.dpw).Select(t =>
                 new {
                   i = t.roll.Instrument, o = t.roll.DateWithShort, t.days, bid = t.bid.AutoRound2(3), perc = t.perc.ToString("0")
                   , dpw = t.dpw.ToInt()
                   , amount = t.amount.ToInt()
                 , exp = t.roll.LastTradeDateOrContractMonth2.Substring(4), d = t.delta.Round(1)
                 }).ToArray());
               });
            };
            Action straddles = () =>
              underContracts
                .ForEach(underContract => {
                  am.CurrentStraddles(CacheKey(underContract), strikeLevel.GetValueOrDefault(double.NaN), expirationDaysSkip, numOfCombos, gap)
                  .Merge(bookStraddle)
                  .SelectMany(c => c)
                  .ToArray()
                  .Select(ts => {
                    var cs = ts.Select(t => {
                      var d = DaysTillExpiration(t.combo.contract.Expiration);
                      var mp = t.marketPrice;
                      var breakEven = t.combo.contract.BreakEven(mp.bid).ToArray();
                      return new {
                        i = t.combo.contract.Key,
                        l = t.combo.contract.WithDate,
                        d = (t.combo.contract.IsFutureOption ? d.Round(1) + " " : ""),
                        mp.bid,
                        mp.ask,
                        avg = mp.ask.Avg(mp.bid),
                        time = mp.time.ToString("HH:mm:ss"),
                        delta = t.deltaBid,
                        strike = t.strikeAvg,
                        perc = (mp.bid / t.underPrice * 100),
                        strikeDelta = t.strikeAvg - strikeLevel.GetValueOrDefault(t.underPrice),
                        be = new { t.breakEven.up, t.breakEven.dn },
                        isActive = false,
                        maxPlPerc = mp.bid * quantity * t.combo.contract.ComboMultiplier / am.Account.Equity * 100 / d,
                        maxPL = mp.bid * quantity * t.combo.contract.ComboMultiplier,
                        underPL = 0,
                        greekDelta = mp.delta,
                        mp.theta,
                        breakEven
                      };
                    });
                    return cs.Distinct(x => x.i).OrderByDescending(s => s.strikeDelta).ToArray();
                    cs = strikeLevel.HasValue && true
                    ? cs.OrderByDescending(x => x.strikeDelta)
                    : cs.OrderByDescending(x => x.delta);
                    return cs.Take(numOfCombos).OrderByDescending(x => x.strike).ToArray();
                  })
                  .Subscribe(b => base.Clients.Caller.butterflies(b));
                });
            var sl = strikeLevel.GetValueOrDefault(double.NaN);

            Action<string> currentOptions = (map) => {

              unders.ForEach(under => {
                var underContract = under.Contract;
                var underPrice = under.price.avg;
                var noc = new[] { "C", "P" }.Contains(map) ? numOfCombos : 0;
                var maxAbs = underPrice * numOfCombos / 1000;
                Func<Contract, bool> filter = c => map.Contains(c.Right) && c.Strike.Abs(underPrice) <= maxAbs;
                am.CurrentOptions(CacheKey(underContract), sl, expirationDaysSkip, 100, filter)
                  .Merge(bookStraddle)
                  .SelectMany(c=>c)
                  .ToArray()
                .Select(ts => {
                  var exp = ts.Select(t => new { dte = DateTime.Now.Date.GetWorkingDays(t.option.Expiration), exp = t.option.LastTradeDateOrContractMonth2 }).Take(1).ToArray();
                  var options = ts
                  .Select(t => {
                    var mp = t.marketPrice;
                    var option = t.combo.contract;
                    var maxPL = t.marketPrice.avg * quantity * option.ComboMultiplier;
                    var strikeDelta = t.strikeAvg - t.underPrice;
                    var _sd = t.strikeAvg - sl.IfNaNOrZero(t.underPrice);
                    var pl = t.deltaBid * quantity * option.ComboMultiplier;
                    var anoPL = MathExtensions.AnnualRate(am.Account.Equity, am.Account.Equity + pl, DateTime.Now, t.combo.contract.Expiration).rate * 100;
                    return new {
                      i = option.Key,
                      l = option.WithDate,
                      mp.bid,
                      mp.ask,
                      avg = mp.ask.Avg(mp.bid),
                      time = mp.time.ToString("HH:mm:ss"),
                      delta = t.deltaBid,
                      strike = t.strikeAvg,
                      perc = (mp.ask.Avg(mp.bid) / t.underPrice * 100).IfNotSetOrZero(0),
                      strikeDelta,
                      be = new { t.breakEven.up, t.breakEven.dn },
                      isActive = false,
                      cp = option.Right,
                      maxPlPerc = anoPL, //t.deltaBid * quantity * option.ComboMultiplier / am.Account.Equity * 100 / exp[0].dte,
                      maxPL,
                      _sd,
                      greekDelta = mp.delta,
                      mp.theta,
                      t.breakEven

                    };
                  })
                  .OrderBy(t => t.strike.Abs(sl))
                  .ToArray();

                  var puts = options.Where(t => t.cp == "P" && (useNaked ? t._sd <= 5 : t._sd >= -5));
                  var calls = options.Where(t => t.cp == "C" && (useNaked ? t._sd >= -5 : t._sd <= 5));
                  var combos = options.Where(t => t.cp.IsNullOrEmpty());
                  //return (exp, b: options.OrderByDescending(x => x.strike));
                  return (exp, b: combos.Concat(calls.OrderByDescending(x => x.strike)).Concat(puts.OrderByDescending(x => x.strike)).ToArray());
                })
                .Subscribe(t => {
                  Clients.Caller.bullPuts(t.b);
                  Clients.Caller.stockOptionsInfo(t.exp);
                });
              }
              );

            };

            #region currentBullPut
            Action currentBullPut = () =>
          underContracts
            .ForEach(symbol
            => am.CurrentBullPuts(CacheKey(symbol), strikeLevel.GetValueOrDefault(double.NaN), expirationDaysSkip, 3, gap)
            .Select(ts => ts
              .Select(t => new {
                i = t.instrument,
                t.bid,
                t.ask,
                avg = t.ask.Avg(t.bid),
                time = t.time.ToString("HH:mm:ss"),
                t.delta,
                strike = t.strikeAvg,
                perc = (t.ask.Avg(t.bid) / t.underPrice * 100),
                strikeDelta = t.strikeAvg - t.underPrice,
                be = new { t.breakEven.up, t.breakEven.dn },
                isActive = false,
                maxPlPerc = t.bid * quantity * t.combo.contract.ComboMultiplier / am.Account.Equity * 100
                / DaysTillExpiration(t.combo.contract.Expiration),
                maxPL = t.bid * quantity * t.combo.contract.ComboMultiplier
              })
            .OrderByDescending(t => t.delta)
            //.ThenBy(t => t.i)
            )
           .Subscribe(b => base.Clients.Caller.options(b)));
            #endregion

            #region openOrders
            var orderMap = MonoidsCore.ToFunc((AccountManager.OrderContractHolder oc, double ask, double bid) => new {
              i = new[] { oc.contract.DateWithShort }
               .Where(_ => am.ParentHolder(oc).Select(p => p.isDone).DefaultIfEmpty(true).Single())
               .Concat(oc.order.Conditions.ToTexts()).Flatter(" & ")
              , id = oc.order.OrderId
              , f = oc.status.filled
              , r = oc.status.remaining
              , lp = !oc.order.IsLimit ? 0 : oc.order.LmtPrice.IfNotSetOrZero(oc.order.AuxPrice).IfNotSetOrZero(0).Round(2)
              , p = (oc.order.Action == "BUY" ? ask : bid).Round(2)
              , a = oc.order.Action.Substring(0, 1)
              , s = oc.status.status.AllCaps()
              , c = oc.order.ParentId != 0
              , e = oc.ShouldExecute
              , gat = oc.order.GoodAfterTime
              , pc = oc.order.Conditions.SelectMany(c => c.ParsePriceCondition()).Select(c => c.price).ToArray()
            }); ;
            Action openOrders = () =>
              (from oc in am.OrderContractsInternal.Items.ToObservable()
               where !oc.isFilled
               from p in oc.contract.ReqPriceSafe().DefaultIfEmpty()
               select (oc, x: orderMap(oc, p.ask, p.bid))
               ).ToArray()
              .Select(a => a.OrderBy(x => x.oc.order.ParentId.IfZero(x.oc.order.OrderId)).ThenBy(x => x.oc.order.ParentId).ToArray())
              .Subscribe(b => base.Clients.Caller.openOrders(b.Select(x => x.x)));

            if(!pair.IsNullOrWhiteSpace())
              _currentCombo.Push(() => {
                if(optionTypeMap.Contains("S"))
                  straddles();
                currentOptions(optionTypeMap);
                openOrders();
                rollOvers();
                ComboTrades();
                if(selectedCombos.Any())
                  SelectedCombos();
                else
                  TradesBreakEvens();
                CurrentHedges();
                //currentBullPut();
              }
              );
            #endregion

            //base.Clients.Caller.liveCombos(am.TradeStraddles().ToArray(x => new { combo = x.straddle, x.netPL, x.position }));
            void ComboTrades() =>
            am.ComboTrades(1, selectedCombos)
              .ToArray()
              .Subscribe(cts => {
                try {
                  var combos = cts
                  .Where(ct => ct.position != 0)
                  .OrderByDescending(ct => ct.contract.IsBag)
                  .ThenBy(ct => ct.contract.FromDetailsCache().Select(cd => cd.UnderSymbol.IfEmpty(ct.contract.Instrument)).FirstOrDefault())
                  .ThenBy(ct => ct.contract.Legs().Count())
                  .ThenBy(ct => ct.contract.LastTradeDateOrContractMonth2)
                  .ThenBy(ct => ct.contract.Right)
                  //.ThenBy(ct => ct.contract.IsOption)
                  .ToArray(x => {
                    var hasStrike = x.contract.HasOptions;
                    var delta = (hasStrike ? x.strikeAvg - x.underPrice : x.closePrice.Abs() - x.openPrice.Abs()) * (-x.position.Sign());
                    var contract = x.contract;
                    var breakEven = contract.BreakEven(-x.openPrice).ToArray();
                    var profit = tm.Strategy == Strategies.HedgeA ? tm.ExitGrossByHedgePositions : x.profit;
                    var red = "#ffd3d9";
                    var green = "chartreuse";
                    string getColor() => x.StrikeColor ? green : red;
                    return new {
                      combo = x.contract.Instrument
                      , i = x.contract.Key
                      , l = x.contract.ShortWithDate
                      , netPL = x.pl - x.Commission
                      , x.position
                        , x.closePrice
                        , x.price.bid, x.price.ask
                        , x.close
                        , delta
                        , x.openPrice
                        , x.takeProfit
                        , profit
                        , x.orderId
                        , exit = 0, exitDelta = 0
                        , pmc = x.pmc.ToInt()
                        , mcu = x.mcUnder.ToInt()
                        , color = getColor()
                        , ic = x.contract.IsOptionsCombo
                        , breakEven
                    };
                  });

                  base.Clients.Caller.liveCombos(combos);
                } catch(Exception exc) {
                  Log = exc;
                }
              }, exc => {
                Log = exc;
              });

            void TradesBreakEvens() =>
              am.TradesBreakEvens(pair)
              .Subscribe(bes => base.Clients.Caller.tradesBreakEvens(bes.Select(be => be.level)));

            void SelectedCombos() {
              var ctsObs = (from ct in am.ComboTrades(1)
                            where ct.contract.IsOption && selectedCombos.Contains(ct.contract.Instrument)
                            select ct//(ct.contract.Strike,ct.openPrice,ct.contract.IsCall)
                         ).ToArray();
              var cmbs = (from cts in ctsObs
                          from sc in selectedCombos
                          where cts.Count(ct => ct.contract.Instrument == sc) == 0
                          from c in Contract.FromCache(sc).SelectMany(l => l.LegsOrMe())
                          where c.IsOption
                          from price in c.ReqPriceSafe()
                          from trades in am.ComboTrades(1).Where(ct => ct.contract == c).ToArray()
                          let debit = trades.Select(t => t.openPrice.Abs()).DefaultIfEmpty(price.bid).Single()
                          select (c.Strike, debit, c.IsCall)
                          );
              var pos = ctsObs.SelectMany(cts => cts.Select(ct => (ct.contract.Strike, debit: ct.openPrice.Abs(), ct.contract.IsCall))).Merge(cmbs)
              .ToArray()
              .Select(AccountManager.BreakEvens);
              pos.Subscribe(bes => base.Clients.Caller.tradesBreakEvens(bes.Where(be => !be.level.IsNaNOrZero()).Select(be => be.level)));
              //AccountManager.BreakEvens()
            }
            void CurrentHedges() {
              var hedgePar = ReadHedgedOther(pair);
              if(hedgePar.IsNullOrEmpty()) return;
              IObservable<CurrentHedge> ByTM() =>
             from ch in CurrentHedgesImpl(pair, am, hedgeQuantity)
               //let h = new { co, ratio = hh.Select(t => t.ratio).First(r => r != 1).AutoRound2(3), context = HID_BYTV/*hh.ToArray(t => t.context).MashDiffs()*/ }
             from price in ch.contract.ReqPriceSafe()
             from priceOpt in ch.optionsBuy.ReqPriceSafe()
             from cts in am.ComboTrades(1).Where(ct => selectedHedge == TradingMacro.HedgeCalcTypes.ByTV && ct.contract.IsHedgeCombo).ToArray()
             from order in am.OrderContractsInternal.Items.Take(1).Select(oh => (oh.contract, quantity: oh.order.TotalQuantity.ToInt(), context: "Open Order")).DefaultIfEmpty()
             let ch0 = new CurrentHedge(TradingMacro.HedgeCalcTypes.ByTV + "", ch.contract.ShortString, ch.quantity, ch.ratio, ch.context, price.ask.Avg(price.bid).AutoRound2(2), ch.contract.Key)
              .SideEffect(_ => cts.Select(ct => (ct.contract, quantity: ct.position, context: "Open Trade"))
              .IfEmpty(() => order.YieldIf(o => o.contract != null))
              .Take(1)
              .DefaultIfEmpty((ch.contract, ch.quantity, context: TradingMacro.HedgeCalcTypes.ByHV + ""))
              .Where(__ => selectedHedge == TradingMacro.HedgeCalcTypes.ByTV)
              .ForEach(t => GetTradingMacros(pair, tml => tml.SetCurrentHedgePosition(t.contract, t.quantity.Abs(), t.context))))
             //let chb = new CurrentHedge("ByTVOB", ch.optionsBuy.ShortSmart, ch.quantity, ch.ratio, "ByTVOB", 0, ch.optionsBuy.Key)
             //let chs = new CurrentHedge("ByTVOS", ch.optionsSell.ShortSmart, ch.quantity, ch.ratio, "ByTVOS", 0, ch.optionsSell.Key)
             from ch01 in new[] { ch0/*, chb, chs*/ }
             select ch01;
              GetCurrentHedgeTMs()
               .ToArray()
               .FirstAsync()
               //.Merge(CurrentHedgesTM(false).SelectMany(c => c))
               //.ToArray()
               .Subscribe(hh => base.Clients.Caller.hedgeCombo(hh.OrderBy(h => h.id)));
            }
          }
        } else GetCurrentHedgeTMs()
          .ToArray()
          .Subscribe(ch => base.Clients.Caller.hedgeCombo(ch.OrderBy(h => h.id)));

        bool CompHID(TradingMacro tml, string hid) => false;// selectedHedge.Contains(hid);
        IObservable<CurrentHedge> GetCurrentHedgeTMs() => GetTradingMacros(pair,
          tml => /*CurrentHedgesTM1(tml, tmh => tmh.CurrentHedgesByHV(), HedgeCalcTypeContext(tml, TradingMacro.HedgeCalcTypes.ByHV))
          .Merge(*/
            CurrentHedgesTM1(tml, tmh => tmh.CurrentHedgesByPositions(0), HedgeCalcTypeContext(tml, TradingMacro.HedgeCalcTypes.ByPos), hedgeQuantity)
          /*)*/
          .Merge(CurrentHedgesTM1(tml, tmh => tmh.CurrentHedgesByPositions(1), HedgeCalcTypeContext(tml, TradingMacro.HedgeCalcTypes.ByPoss), hedgeQuantity))
          //.Merge(CurrentHedgesTM1(tml, tmh => tmh.CurrentHedgesByPositionsGross(0), HedgeCalcTypeContext(tml, TradingMacro.HedgeCalcTypes.ByGross)))
          //.Merge(CurrentHedgesTM1(tml, tmh => tmh.CurrentHedgesByPositionsGross(1), HedgeCalcTypeContext(tml, TradingMacro.HedgeCalcTypes.ByGrosss)))
          .Merge(CurrentHedgesTM1(tml, tmh => tmh.CurrentHedgesByTradingRatio(0), HedgeCalcTypeContext(tml, TradingMacro.HedgeCalcTypes.ByTR), hedgeQuantity))
          ).Merge();

        var tm1 = tm.TradingMacroM1().DefaultIfEmpty(tm).ToArray();
        var distFromHigh = tm1.Select(tmM1 => tmM1.RatesMax / tm.CurrentPrice?.Average - 1).SingleOrDefault();
        var distFromLow = tm1.Select(tmM1 => tmM1.RatesMax / tmM1.RatesMin - 1).SingleOrDefault();
        var digits = 2;// tm.Digits();
        return new {
          tm.TradingRatio,
          tm.OptionsDaysGap,
          Strategy = tm.Strategy + "",
          DistanceFromHigh = distFromHigh,
          DistanceFromLow = distFromLow,
          HedgeCalcType = tm.HedgeCalcType + "" + tm.HedgeCalcIndex,
          TrendEdgesLastDate = RUN_EDGE_TREND ? GetAccountManager()?.TrendEdgesLastDate : DateTime.Now,
          PriceAvg1 = tm1.Select(tml => tml.TLLime?.PriceAvg1.Round(digits)).FirstOrDefault()
        };
        string HedgeCalcTypeContext(TradingMacro tml, TradingMacro.HedgeCalcTypes hct) => hct.ToString() + tml.PairIndex;
        ////
        IObservable<CurrentHedge> CurrentHedgesTM1(TradingMacro tml, Func<TradingMacro, CURRENT_HEDGES> getHedges, string id, int hQuantity) {
          var hedgePar = ReadHedgedOther(pair);
          if(hedgePar.IsNullOrEmpty()) return Observable.Empty<CurrentHedge>();
          var baseUnits = GetTradingMacros(pair).Concat(GetTradingMacros(hedgePar)).Where(t => t.IsActive).Select(_tm => _tm.BaseUnitSize).ToArray();
          //var hh0 = tm.TradingMacroM1(getHedges).Concat().ToArray();
          var hh0 = getHedges(tml);
          hh0.ForEach(h => h.contract.FromCache().RunIfEmpty(() => h.contract.ReqContractDetailsCached().Subscribe()));
          if(hh0.Count < 2) return Observable.Empty<CurrentHedge>();
          var combo0 = MakeHedgeComboSafe(hQuantity, hh0[0].contract, hh0[1].contract, hh0[0].ratio, hh0[1].ratio, IsInVirtual());
          IObservable<MarketPrice> CalcComboPrice()
          => (
          from c in combo0
          from l in c.contract.LegsForHedge(pair)
          from hh in hh0
          where l.c.ConId == hh.contract.ConId
          select (hh.price, hh.contract.ComboMultiplier, quantiy: (double)l.leg.Quantity, c.multiplier)
              )
              .ToArray()
              .Select(xx => new { xx = xx.Select(x => (x.price, x.ComboMultiplier, x.quantiy)).ToArray(), xx[0].multiplier })
              .Select(x => x.xx.CalcHedgePrice(x.multiplier).With(p => new MarketPrice(p, p, tml.ServerTime, delta: 1.0, double.NaN)));
          try {
            var hh = hh0.Zip(baseUnits, (h, BaseUnitSize) => new { h.contract, h.ratio, h.price, h.context, BaseUnitSize }).ToArray();
            return
            (from q in Observable.Return(hQuantity)
             where q != 0
             from c in MakeHedgeComboSafe()
             from p in IsInVirtual() ? CalcComboPrice() : c.contract.ReqPriceSafe().DefaultIfEmpty()
             let rc = new { ratio = (hh[0].ratio.Abs() < 1 ? 1 / hh[0].ratio : hh[1].ratio).Abs().AutoRound2(3), context = id/* hh.ToArray(t => t.context).MashDiffs()*/ }
             let contract = c.contract.ShortString
             select new CurrentHedge(id, contract, c.quantity, rc.ratio, rc.context, p.bid.Avg(p.ask).ToInt(), c.contract.Key)
             ).Catch((Exception exc) => {
               Log = exc;
               return Observable.Empty<CurrentHedge>();
             });
            /// Locals
            IObservable<HedgeCombo> MakeHedgeComboSafe() => AccountManager.MakeHedgeComboSafe(hQuantity, hh[0].contract, hh[1].contract, hh[0].ratio, hh[1].ratio, IsInVirtual());
          } catch(Exception exc) {
            Log = exc;
            return Observable.Empty<CurrentHedge>();
          }
        }
      }).WhenAllSequiential()).ToArray();
      return task;
    }
    public class CurrentHedge {
      public CurrentHedge(string id, string contract, double quantity, double ratio, string context, double price, string key) {
        this.id = id;
        this.contract = contract;
        this.ratio = ratio;
        this.quantity = quantity;
        this.context = context;
        this.price = price;
        this.key = key;
      }

      public string id { get; }
      public string contract { get; }
      public double ratio { get; }
      public double quantity { get; }
      public string context { get; }
      public double price { get; }
      public string key { get; }
    }

    IObservable<(Contract contract, int quantity, double ratio, string context, Contract optionsBuy, Contract optionsSell)> CurrentHedgesImpl(string pair, AccountManager am, int quantity) {
      var hedgePar = ReadHedgedOther(pair);
      return from tm in UseTraderMacro(pair).ToObservable()
             from corr in tm.TMCorrelation(0).ToObservable()
             from hh in am.CurrentHedges(pair, hedgePar, "", c => c.ShortWithDate2, tm.HedgeTimeValueDays, corr > 0)
             where quantity != 0 && hh.Any()
             from optionCombos in hh.CurrentOptionHedges(quantity)
             let combo = hh.Select(h => (h.contract, h.ratio)).MakeHedgeCombo(quantity)
             let h = (combo.contract
             , combo.quantity
             , ratio: hh.Select(t => t.ratio).First(r => r != 1).AutoRound2(3)
             , context: TradingMacro.HedgeCalcTypes.ByTV + ""/*hh.ToArray(t => t.context).MashDiffs()*/
             , optionCombos.buy
             , optionCombos.sell)
             select h;
    }
    [BasicAuthenticationFilter]
    public async Task<string[]> RollTrade(string currentSymbol, string rollSymbol, int rollQuantity, bool isTest) {
      var res = await GetAccountManager().OpenRollTrade(currentSymbol, rollSymbol, rollQuantity, isTest).SelectMany(t => t).ToArray();
      return res.Where(t => t.error.HasError)
        .Do(e => LogMessage.Send(e.error.exc))
        .Select(e => e.error.exc.Message)
        .ToArray();
    }
    [BasicAuthenticationFilter]
    public void CancelOrder(int orderId) {
      var am = GetAccountManager();
      am.CancelOrder(orderId)
        .Subscribe(_ => {
          am.OrderContractsInternal.Items.ByOrderId(orderId).Where(o => o.isInactive)
          .ForEach(och => am.OrderContractsInternal.Remove(och.order.PermId));
        });
    }
    [BasicAuthenticationFilter]
    public async Task<object[]> UpdateOrderPriceCondition(int orderId, double price) {
      var am = GetAccountManager();
      var order = am.OrderContractsInternal.Items.First();
      order.order.Conditions.Cast<PriceCondition>().ForEach(pc => pc.Price = price);
      var ret = (from co in am.CancelOrder(order.order.OrderId)
                 from po in am.PlaceOrder(order.order.SideEffect(_ => _.OrderId = 0), order.contract)
                 from h in po.value
                 select new { po.error, prder = h.order + "" }).ToArray();

      return await ret;
    }

    public async Task<object[]> OpenStrategyOption(string option, int quantity, double level, double profit) =>
      await (from contract in DataManager.IBClientMaster.ReqContractDetailsCached(option).Select(cd => cd.Contract)
             from under in contract.UnderContract
             let openCond = under.PriceCondition(level.Round(2), contract.IsPut)
             let tpCond = under.PriceCondition((level + profit * (contract.IsCall ? 1 : -1)).Round(2), contract.IsCall)
             let dateAfter = _isTest ? DateTime.Now.AddHours(10) : DateTime.MinValue
             //select new { contract, quantity, openCond, tpCond, dateAfter }
             from ots in GetAccountManager().OpenTrade(contract, quantity, 0, 0, true, default, dateAfter, openCond, tpCond)
             from ot in ots
             select new { order = ot.holder + "", error = ot.error + "" }
       ).ToArray();

    public async Task<object[]> CallByBS(string pair) {
      var tm = UseTraderMacro(pair).Single();
      var hasStrategy = tm.Strategy.HasFlag(Strategies.Universal);
      var def = new string[0];
      if(!hasStrategy) return def;
      var bs = new { b = tm.BuyLevel.Rate, s = tm.SellLevel.Rate };
      var levelCall = bs.b;
      var levelPut = bs.s;
      if(levelCall.IsNaNOrZero()) return def;
      var am = GetAccountManager();
      var expSkip = tm.ServerTime.Hour > 16 ? 1 : 0;
      var calls = from os in CallsPuts()
                  from cp in os
                  group cp by cp.IsCall into g
                  from o in g.OrderBy(OrderBy).Take(3).TakeLast(1)
                  select new { l = Level(o), o = o.LocalSymbol, cp = o.Right, lb = o.ShortWithDate };
      return await calls.ToArray();

      double Level(Contract c) => c.IsCall ? levelCall : levelPut;
      IObservable<Contract[]> CallsPuts() => Calls().Merge(Puts()).ToArray();
      IObservable<Contract> Calls() =>
        am.CurrentOptions(tm.Pair, levelCall, expSkip, 4, c => c.IsCall && c.Strike < levelCall).SelectMany(a => a.Select(t => t.option));
      IObservable<Contract> Puts() =>
        am.CurrentOptions(tm.Pair, levelPut, expSkip, 4, c => c.IsPut && c.Strike > levelPut).SelectMany(a => a.Select(t => t.option));
      double OrderBy(Contract c) => c.IsCall ? 1 / c.Strike : c.Strike;
      bool Filter(Contract c, double level) => c.IsCall && c.Strike < level || c.IsPut && c.Strike > level;
    }

    public async Task<object[]> CallByBS_Old(string pair) {
      var tm = UseTraderMacro(pair).Single();
      var hasStrategy = tm.Strategy.HasFlag(Strategies.Universal);
      var def = new string[0];
      if(!hasStrategy) return def;
      var bs = new { b = tm.BuyLevel.Rate, s = tm.SellLevel.Rate };
      var levelCall = bs.b;
      var levelPut = bs.s;
      if(levelCall.IsNaNOrZero()) return def;
      var am = GetAccountManager();
      var expSkip = tm.ServerTime.Hour > 16 ? 1 : 0;
      var calls = from os in am.CurrentOptions(tm.Pair, levelCall, expSkip, 5, c => c.IsCall && c.Strike > levelCall)
                  from o in os.OrderBy(_ => _.strikeAvg).Take(3).TakeLast(1)
                  select new { l = levelCall, o = o.option.LocalSymbol, cp = o.option.Right, lb = o.option.ShortWithDate };
      var puts = from os in am.CurrentOptions(tm.Pair, levelPut, expSkip, 5, c => c.IsPut && c.Strike < levelPut)
                 from o in os.OrderByDescending(_ => _.strikeAvg).Take(3).TakeLast(1)
                 select new { l = levelPut, o = o.option.LocalSymbol, cp = o.option.Right, lb = o.option.ShortWithDate };
      return await calls.Merge(puts).ToArray();
    }


    [BasicAuthenticationFilter]
    public void OpenCoveredOption(string pair, int quantity, double? price) {
      Contract.Contracts.TryGetValue(pair, out var contract);
      if(contract == null)
        throw new Exception(new { pair, not = "found" } + "");
      GetAccountManager().OpenCoveredOption(contract, quantity, price.GetValueOrDefault());
    }
    [BasicAuthenticationFilter]
    public void OpenCovered(string pair, string option, int quantity, double price) {
      GetAccountManager().OpenCoveredOption(pair, option, quantity, price);
    }
    [BasicAuthenticationFilter]
    public async Task<object[]> CloseCombo_New(string instrument, double? conditionPrice) {
      var obs =
      (from am in Observable.Return(GetAccountManager())
       from ct in am.ComboTrades(1)
       where ct.orderId != 0
       from ochs in am.UseOrderContracts(orderContracts => orderContracts.ByOrderId(ct.orderId))
       from och in ochs
       from under in ct.contract.UnderContract
       where ct.contract.Instrument == instrument
       select (ct.contract, ct.position, ct.orderId, ct.closePrice, under, och, am)
       );
      var res = await obs.SelectMany(c => {
        c.och.order.OrderType = "LMT";
        c.och.order.LmtPrice = c.closePrice;
        // TODO Update UI to set catTrade to true
        return (from po in c.am.PlaceOrder(c.och.order, c.contract)
                select new { order = po.value.Flatter(";"), po.error }
                );
      }).ToArray();
      return res;
    }
    [BasicAuthenticationFilter]
    public async Task<string[]> CloseCombo(string pair, string instrument, double? conditionPrice, bool isTest, IList<string> selection) {
      var res = (await
      (from am in Observable.Return(GetAccountManager())
       from ct in am.ComboTrades(1, selection)
       from under in pair.ReqContractDetailsCached().Select(cd => cd.Contract)
       from underPrice in under.ReqPriceSafe()
       where ct.contract.Instrument == instrument
       select new { ct.contract, ct.position, ct.orderId, ct.closePrice, under, underPrice, am }
       )
      .SelectMany(c => {
        if(c.contract.IsStocksCombo) {
          return (from p in c.contract.ReqPriceSafe().Select(ab => -c.position > 0 ? ab.ask : ab.bid)
                  from ots in c.am.OpenTradeWithAction(o => o.Transmit = !isTest, c.contract, -c.position, p)
                  from ot in ots
                  select ot.error
                  ).Catch<ErrorMessage, GenericException<ErrorMessage>>(exc => Observable.Return(exc.Context));

          return (from ctc in c.contract.LegsEx().ToObservable()
                  from ots in c.am.OpenTrade(ctc.contract, ctc.leg.Quantity * -c.position)
                  from ot in ots
                  select ot.error
                  ).Catch<ErrorMessage, GenericException<ErrorMessage>>(exc => Observable.Return(exc.Context));
        }
        if(c.orderId != 0) {
          var och = c.am.UseOrderContracts(orderContracts => orderContracts.ByOrderId(c.orderId)).Concat().SingleOrDefault();
          if(och == null)
            throw new Exception($"OrderContractHoled with OrderID:{c.orderId} not found");
          var quantity = och.order.TotalQuantity.ToInt() * (och.order.IsBuy ? 1 : -1);
          var isMarket = c.contract.IsBag;
          if(isMarket) {
            return (from co in c.am.CancelOrder(c.orderId)
                    where !co.error.HasError.SideEffect(he => { if(he) throw new GenericException<ErrorMessage>(co.error); })
                    from ctc in c.contract.LegsEx()
                    from ots in c.am.OpenTradeWithAction(o => o.Transmit = !isTest, ctc.contract, ctc.leg.Quantity * quantity)
                    from ot in ots
                    select ot.error
                   ).Catch<ErrorMessage, GenericException<ErrorMessage>>(exc => Observable.Return(exc.Context));
          }
          och.order.OrderType = isMarket ? "MKT" : "LMT";
          och.order.LmtPrice = isMarket ? 0 : c.closePrice;
          och.order.Transmit = !isTest;
          return c.am.PlaceOrder(och.order, och.contract).Select(m => m.error);
        } else {
          //am.CancelAllOrders("CloseCombo");
          var isBuy = c.position.Sign() < 0;
          bool? isMore = conditionPrice.HasValue
          ? IsMore(conditionPrice.Value, c.underPrice.avg, isBuy, c.contract.DeltaSignCombined)
          : (bool?)null;
          return from tt in c.am.OpenTradeWithAction(order => order.Transmit = !isTest, c.contract, -c.position
          , isMore.HasValue || c.contract.IsCallPut || c.contract.IsHedgeCombo ? 0 : c.closePrice
          , 0.0, false, default, default
          , isMore.HasValue ? c.under.PriceCondition(conditionPrice.Value, isMore.Value, false) : default)
                 from t in tt
                 select t.error;
        }
      }).ToArray()).Where(t => !t.IsDefault()).ToArray();
      return res.Where(e => e.HasError).Do(e => Log = e.exc ?? new Exception(e.ToString()))
      .Select(e => e.exc?.Message ?? e.ToString())
      .DefaultIfEmpty($"Closing {instrument} all good.")
      .ToArray();
    }
    static bool IsMore(double conditionPrice, double currentPrice, bool isBuy, int deltaSign) {
      var mul = deltaSign * (isBuy ? 1 : -1);
      if(mul > 0 && conditionPrice > currentPrice) throw new Exception($"Codition price ${conditionPrice.Round(2)} < current price {currentPrice.Round(2)}");
      if(mul < 0 && conditionPrice < currentPrice) throw new Exception($"Codition price ${conditionPrice.Round(2)} < current price {currentPrice.Round(2)}");
      return conditionPrice > currentPrice;
    }
    [BasicAuthenticationFilter]
    public void CancelAllOrders() {
      var am = GetAccountManager();
      am?.CancelAllOrders(nameof(CancelAllOrders));
      GetTradingMacros().ForEach(tm => tm.PendingEntryOrders.Select(po => po.Key).ToList().ForEach(k => tm.PendingEntryOrders.Remove(k)));
    }
    public object[] ReadContractsCache() {
      return Contract.Cache()
        .OrderBy(x => x.Expiration)
        .ThenBy(x => x.ComboStrike())
        .ThenBy(x => x.IsPut)
        .Select(c => new { c.Instrument, c.ConId })
        .ToArray();
    }
    public object[] ReadActiveRequests() {
      return MarketDataManager.ActiveRequests
        .OrderBy(x => x.Value.contract.Instrument)
        .ThenBy(x => x.Key)
        .Select(x => new { x.Value.contract.ShortWithDate, x.Key, x.Value.price.Bid, x.Value.price.Ask })
        .ToArray();
    }
    public void CleanActiveRequests() => MarketDataManager.ActiveRequestCleaner();
    #endregion

    #region TradeConditions
    public string[] ReadTradingConditions(string pair) {
      return UseTraderMacro(pair, tm => tm.TradeConditionsAllInfo((tc, p, name) => name)).Concat().ToArray();
    }

    public string[] GetTradingConditions(string pair) {
      return UseTraderMacro(pair, tm => tm.TradeConditionsInfo((tc, p, name) => name)).Concat().ToArray();
    }
    [BasicAuthenticationFilter]
    public void SetTradingConditions(string pair, string[] names) {
      UseTraderMacro(pair, tm => { tm.TradeConditionsSet(names); });
    }
    public Dictionary<string, Dictionary<string, bool>> GetTradingConditionsInfo(string pair) {
      var d = UseTraderMacro(pair, tm => GetTradeConditionsInfo(tm));
      return d.DefaultIfEmpty(new Dictionary<string, Dictionary<string, bool>>()).Single();
    }

    static object _getTradeConditionsInfoGate = new object();
    private static Dictionary<string, Dictionary<string, bool>> GetTradeConditionsInfo(TradingMacro tm) {
      lock(_getTradeConditionsInfoGate) {
        var tt = tm.UseRates(ra => ra.IsEmpty() || !tm.IsRatesLengthStable).DefaultIfEmpty(true).SingleOrDefault();
        if(tt) return new Dictionary<string, Dictionary<string, bool>>();
        var eval = tm.TradeConditionsInfo((d, p, t, c) => new { c, t, d = d() }).ToList();
        return eval
          .GroupBy(x => x.t)
          .Select(g => new { g.Key, d = g.ToDictionary(x => x.c, x => x.d.HasAny()) })
          .ToDictionary(x => x.Key + "", x => x.d);
      }
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
      UseTraderMacro(pair, tm => tm.CleanStrategyParameters());
    }
    public async Task<object[]> ReadStrategies(string pair) {
      return (await (UseTraderMacro(pair
        , async tm => (await RemoteControlModel.ReadStrategies(tm, (nick, name, content, uri, diff)
          => new { nick, name, uri, diff, isActive = diff.IsEmpty() })).ToArray()
        )).WhenAllSequiential()).First();
    }
    [BasicAuthenticationFilter]
    public Task SaveStrategy(string pair, string nick) {
      if(string.IsNullOrWhiteSpace(nick))
        throw new ArgumentException("Is empty", "nick");
      return UseTraderMacro(pair, tm => RemoteControlModel.SaveStrategy(tm, nick)).WhenAllSequiential();
    }
    public Task RemoveStrategy(string name, bool permanent) => RemoteControlModel.RemoveStrategy(name, permanent);
    [BasicAuthenticationFilter]
    public async Task UpdateStrategy(string pair, string name) {
      await UseTradingMacro2(pair, 0, async tm => {
        tm.IsTradingActive = false;
        await RemoteControlModel.LoadStrategy(tm, name);
      }).WhenAllSequiential();
    }
    [BasicAuthenticationFilter]
    public async Task LoadStrategy(string pair, string strategyPath) {
      await UseTradingMacro2(pair, 0, async tm => {
        tm.IsTradingActive = false;
        await RemoteControlModel.LoadStrategy(tm, strategyPath);
      }).WhenAllSequiential();
    }
    [BasicAuthenticationFilter]
    public void SaveTradingMacros(string tradingMacroName) {
      if(string.IsNullOrWhiteSpace(tradingMacroName))
        throw new ArgumentException("Is empty", nameof(tradingMacroName));
      if(GlobalStorage.TradingMacroNames.Any(tm => tm.ToLower() == tradingMacroName.ToLower()))
        throw new ArgumentException($"Trading Macro[{tradingMacroName}] already exists");
      remoteControl.Value.SaveTradingMacros(tradingMacroName);
    }
    #endregion

    #region ReplayArguments
    public object ReadReplayArguments(string pair) {
      var ra = UseTraderMacro(pair, tm => new {
        remoteControl.Value.ReplayArguments.DateStart,
        isReplayOn = tm.IsInPlayback,
        remoteControl.Value.ReplayArguments.LastWwwError
      });
      remoteControl.WithNotNull(r => r.Value.ReplayArguments.LastWwwError = "");
      return ra.Single();
    }
    public object StartReplay(string pair, string startWhen) {
      SetReplayDate(startWhen);
      remoteControl.Value.ReplayArguments.IsWww = true;
      UseTraderMacro(pair, tm => remoteControl.Value.StartReplayCommand.Execute(tm));
      return ReadReplayArguments(pair);
    }

    private void SetReplayDate(string startWhen) {
      TimeSpan ts;
      DateTime dateStart = TimeSpan.TryParse(startWhen, out ts)
        ? DateTime.Now.Subtract(ts)
        : DateTime.Parse(startWhen);
      remoteControl.Value.ReplayArguments.DateStart = dateStart;
    }

    public object StopReplay(string pair, string startWhen) {
      SetReplayDate(startWhen);
      remoteControl.Value.ReplayArguments.MustStop = true;
      return ReadReplayArguments(pair);
    }
    #endregion
    public object GetWwwInfo(string pair) {
      return UseTraderMacro(pair, tm => tm.WwwInfo()).DefaultIfEmpty(new { }).First();
    }

    public void Send(string pair, string newInyterval) {
      try {
        App.SignalRInterval = int.Parse(newInyterval);
        App.ResetSignalR();
      } catch(Exception exc) {
        LogMessage.Send(exc);
      }
    }
    public void CloseTrades(string pair) {
      try {
        UseTraderMacro(pair)
          .AsParallel()
          .ForAll(tm => tm.CloseTrades(null, "SignalR: CloseTrades"));
      } catch(Exception exc) {
        LogMessage.Send(exc);
      }
    }
    public void CloseTradesAll(string pair) {
      try {
        trader.Value.TradesManager.GetTrades().ForEach(t => trader.Value.TradesManager.ClosePair(t.Pair, null));
        trader.Value.GrossToExitSoftReset();
      } catch(Exception exc) {
        LogMessage.Send(exc);
      }
    }
    public void MoveTradeLevel(string pair, bool isBuy, double pips) {
      try {
        GetTradingMacro(pair).ForEach(tm => tm.MoveBuySellLeve(isBuy, pips));
      } catch(Exception exc) {
        LogMessage.Send(exc);
      }
    }
    public void SetTradeRate(string pair, bool isBuy, double price) {
      GetTradingMacro(pair).ForEach(tm => tm.SetTradeRate(isBuy, price));
    }
    public void ManualToggle(string pair) {
      try {
        GetTradingMacro(pair).ForEach(tm => tm.ResetSuppResesInManual());
      } catch(Exception exc) {
        LogMessage.Send(exc);
      }
    }
    [BasicAuthenticationFilter]
    public void SetStrategy(string pair, string strategy) {
      UseTradingMacro(pair, tm => tm.Strategy = EnumUtils.Parse<Strategies>(strategy));
    }
    [BasicAuthenticationFilter]
    public bool ToggleIsActive(string pair) {
      return UseTraderMacro(pair, tm => tm.ToggleIsActive()).DefaultIfEmpty().First();
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
    public void Buy(string pair) {
      UseTraderMacro(pair, tm => tm.OpenTrade(true, tm.Trades.IsBuy(false).Lots() + tm.LotSizeByLossBuy, null, "web: buy with reverse"));
    }
    [BasicAuthenticationFilter]
    public void Sell(string pair) {
      UseTraderMacro(pair, tm => tm.OpenTrade(false, tm.Trades.IsBuy(true).Lots() + tm.LotSizeByLossSell, null, "web: sell with reverse"));
    }
    public object[] AskRates(int charterWidth, DateTimeOffset startDate, DateTimeOffset endDate, string pair, BarsPeriodType chartNum) {
      var a = UseTradingMacro2(pair, (int)chartNum
        , tm => remoteControl.Value.ServeChart(charterWidth, startDate, endDate, tm));
      return a.SelectMany(x => x).ToArray();

      //var a = UseTradingMacro2(pair, chartNum
      //  , tm => Task.Run(() => remoteControl.Value.ServeChart(charterWidth, startDate, endDate, tm)));
      //var b = await Task.WhenAll(a);
      //return b.SelectMany(x => x).ToArray();
    }

    static int _sendChartBufferCounter = 0;
    static SendChartBuffer _sendChartBuffer = new SendChartBuffer();

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
      try {
        var date = DateTimeOffset.UtcNow.AddMinutes(-60);
        var countries = new[] { "USD", "US", "GBP", "GB", "EUR", "EMU", "JPY", "JP" };
        return GlobalStorage.UseForexContext(c => c.Event__News.Where(en => en.Time > date && countries.Contains(en.Country)).ToArray());
      } catch {
        return new DB.Event__News[0];
      }
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
      return UseTradingMacro2(pair, chartNum, tm => {
        var props = (IDictionary<string, object>)ts;
        props.ForEach(kv => {
          tm.SetProperty(kv.Key, kv.Value, p => p.GetSetMethod() != null);
        });
        return ReadTradeSettings(pair, chartNum);
      }).DefaultIfEmpty(new { }.ToExpando()).First();
    }
    public ExpandoObject ReadTradeSettings(string pair, int chartNum) {
      return UseTradingMacro2(pair, chartNum, tm => {
        var e = (IDictionary<string, object>)new ExpandoObject();
        Func<object, object> convert = o => o != null && o.GetType().IsEnum ? o + "" : o;
        Func<PropertyInfo, string> dn = pi => pi.GetCustomAttributes<DisplayNameAttribute>().Select(a => a.DisplayName).DefaultIfEmpty(pi.Name).Single();
        tm.GetPropertiesByAttibute<WwwSettingAttribute>(_ => !_.Hide)
          .Where(x => !x.Item1.Group.ToLower().StartsWith("hide"))
          .Select(x => new { x.Item1.Group, p = x.Item2, dn = dn(x.Item2) })
          .OrderBy(x => x.dn)
          .OrderBy(x => x.Group)
          .ForEach(x => e.Add(x.p.Name, new { v = convert(x.p.GetValue(tm)), g = x.Group, x.dn }));
        return e as ExpandoObject;
      }).DefaultIfEmpty(new { }.ToExpando()).First();
    }
    #endregion
    static string MakePair(string pair) { return TradesManagerStatic.IsCurrenncy(pair) ? pair.Substring(0, 3) + "/" + pair.Substring(3, 3) : pair; }
    bool IsEOW(DateTime date) => date.DayOfWeek == DayOfWeek.Friday && date.InNewYork().TimeOfDay > new TimeSpan(16, 0, 0);
    public Trade[] ReadClosedTrades(string pair, bool showAll = false) {
      try {
        var tms = GetTradingMacros(pair).OrderByDescending(tm => tm.BarPeriod).Take(1);
        var rc = remoteControl.Value;
        var trades = rc.GetClosedTrades("").Concat(rc.TradesManager.GetTrades()).ToArray();
        var tradesNew = (
          from tm in tms
          from trade in trades
          from dateMin in showAll ? DateTime.MinValue.Yield() : tm.RatesArray.Take(1).Select(r => r.StartDate)
          where trade.Time >= dateMin
          orderby trade.Time descending
          let rateOpen = tm.RatesArray.FuzzyFinder(trade.Time, (t, r1, r2) => t.Between(r1.StartDate, r2.StartDate)).Take(1)
          .IfEmpty(() => IsEOW(trade.Time) ? tm.RatesArray.TakeLast(1) : new Rate[0]).ToArray()
          let rateClose = tm.RatesArray.FuzzyFinder(trade.TimeClose, (t, r1, r2) => t.Between(r1.StartDate, r2.StartDate)).Take(1)
          .IfEmpty(() => IsEOW(trade.Time) ? tm.RatesArray.TakeLast(1) : new Rate[0]).ToArray().ToArray()
          where showAll || (rateOpen.Any() && (trade.Kind != PositionBase.PositionKind.Closed || rateClose.Any()))
          select (trade, rateOpen, rateClose)
         ).Select(t => {
           var trade = t.trade.Clone();
           t.rateOpen.Select(r => r.PriceAvg).ForEach(p => trade.Open = p/* * trade.BaseUnitSize*/);
           t.rateClose.Select(r => r.PriceAvg).ForEach(p => trade.Close = p/* * trade.BaseUnitSize*/);
           trade.GrossPL = t.trade.GrossPL;
           return trade;
         }).ToArray();
        return tradesNew;
      } catch(Exception exc) {
        LogMessage.Send(exc);
        return new Trade[0];
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
          StDev = value(x.wr.StDev.Round(5), x.wr.StDev < wrStats[0].StDev.v),//.ToString("#0.00"),
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
      var res = wrs.Cast<object>()
        //.Concat(wra.Cast<object>())
        .Concat(wrStats.Cast<object>())
        /*.Concat(wrStd.Cast<object>())*/.ToArray();
      return res;
    }

    public object[] GetAccounting(string pair) {
      var row = MonoidsCore.ToFunc("", (object)null, (n, v) => new { n, v });
      var list = new[] { row("", 0) }.Take(0).ToList();
      if(remoteControl == null) return new object[0];
      var rc = remoteControl.Value;
      var am = rc.MasterModel.AccountModel;
      double CompInt(double profit) => am.Equity.CompoundInteres(profit, 252);
      list.Add(row("BalanceOrg", am.OriginalBalance.ToString("c0")));
      //list.Add(row("Balance", am.Balance.ToString("c0")));
      list.Add(row("Equity", am.Equity.ToString("c0")));
      var more = UseTraderMacro(pair, tm => {
        Trade[] trades = tm.Trades.Concat(remoteControl.Value.TradesManager.GetTrades(tm.PairHedge)).ToArray();
        var ht = tm.HaveTrades();
        var list2 = new[] { row("", 0) }.Take(0).ToList();
        if(trades.Any()) {
          var c = am.CurrentGross > 0 ? "lawngreen" : "lightpink";
          list2.Add(row("CurrentGross", new { v = am.CurrentGross.AutoRound2("$", 2) + (ht ? "/" + (am.ProfitPercent * 100).AutoRound2(2, "%") : ""), c }));

          if(tm.HaveHedgedTrades())
            list2.Add(row("CurrentLot", string.Join("/", trades.GroupBy(t => t.Pair).Select(g => g.Sum(t => t.Position)))));
          else if(ht)
            list2.Add(row("CurrentLot", tm.Trades.Lots() + (ht ? "/" + tm.PipAmount.AutoRound2("$", 2) : "")));
          list2.Add(row($"{pair} Gross", $"{trades.Gross().AutoRound2("$", 2)}@{tm.ServerTime.TimeOfDay.ToString(@"hh\:mm\:ss")}"));
        }
        list2.Add(row("Trades Gross", $"{rc.TradesManager.GetTrades().Gross().AutoRound2("$", 2)}@{tm.ServerTime.TimeOfDay.ToString(@"hh\:mm\:ss")}"));
        if(false && !ht) {
          list2.Add(row("PipAmountBuy", tm.PipAmountBuy.AutoRound2("$", 2) + "/" + (tm.PipAmountBuyPercent * 100).AutoRound2(2, "%")));
          list2.Add(row("PipAmountSell", tm.PipAmountSell.AutoRound2("$", 2) + "/" + (tm.PipAmountSellPercent * 100).AutoRound2(2, "%")));
        }
        if(!tm.Strategy.IsHedge()) {
          list2.Add(row("PipsToMC", (!ht ? 0 : am.PipsToMC).ToString("n0")));
          var lsb = (tm.IsCurrency ? (tm.LotSizeByLossBuy / 1000.0).Floor() + "K/" : tm.LotSizeByLossBuy + "/");
          var lss = (tm.IsCurrency ? (tm.LotSizeByLossSell / 1000.0).Floor() + "K/" : tm.LotSizeByLossSell + "/");
          var lsp = tm.LotSizePercent.ToString("p0");
          list2.Add(row("LotSizeBS", lsb + (lsb == lss ? "" : "" + lss) + lsp));
        }
        var hpos = tm.TradingMacroHedged(tmh => new { p = tmh, po = tmh.PendingEntryOrders }, -1);
        var pos = new[] { new { p = tm, po = tm.PendingEntryOrders } }
        .Concat(hpos).ToArray();
        if(pos.Any())
          list2.Add(row("Pending", pos.SelectMany(po => po.po.Select(kv => $"{po.p}:{kv.Key}")).ToArray().ToJson(false)));
        var oss = rc.TradesManager.GetOrderStatuses();
        if(oss.Any()) {
          var s = string.Join("<br/>", oss.Select(os => $"{os.status}:{os.filled}<{os.remaining}"));
          list2.Add(row("Orders", s));
        }
        if(tm.Strategy.IsHedge())
          list2.Add(row("Hedge Profit", $"{tm.ExitGrossByHedgePositions:c0}"));
        if(false && trader.Value.GrossToExitCalc() != 0) {
          var ca = CompInt(trader.Value.GrossToExitCalc().Abs());
          list2.Add(row("GrossToExit", $"${trader.Value.GrossToExitCalc().AutoRound2(1)}:{((ca) * 100).AutoRound2(3, "%")}"));
          list2.Add(row("grossToExitRaw", trader.Value.GrossToExit));
          list2.Add(row("profitByHedgeRatioDiff", trader.Value.ProfitByHedgeRatioDiff));
        }
        tm.CurrentPut?.ForEach(p =>
        list2.Add(row("Curr Put", p.option.ShortWithDate)));
        tm.CurrentStraddle?.ForEach(p => list2.Add(row("Curr Strdl", $"{p.combo.contract}")));
        tm.StrategyBS.Values.ForEach(b => list2.Add(row("Stgy Buy", b.contract.ShortWithDate + "<" + b.level)));
        return list2;
      }).Concat();
      return list.Concat(more).ToArray();
    }
    double IntOrDouble(double d, double max = 10) {
      return d.Abs() > max ? d.ToInt() : d.Round(1);
    }
    [BasicAuthenticationFilter]
    public void SetTradeCount(string pair, int tradeCount) {
      GetTradingMacro(pair, tm => tm.SetTradeCount(tradeCount));
    }
    [BasicAuthenticationFilter]
    public void SaveGrossToExit(double grossToExit) {
      trader.Value.GrossToExit = grossToExit;
    }
    [BasicAuthenticationFilter]
    public void SaveProfitByHedgeRatioDiff(double profitByRatioDiff) {
      trader.Value.ProfitByHedgeRatioDiff = profitByRatioDiff;
    }
    [BasicAuthenticationFilter]
    public void StopTrades(string pair) { SetCanTradeImpl(pair, false, null); }
    [BasicAuthenticationFilter]
    public void StartTrades(string pair, bool isBuy) { SetCanTrade(pair, true, isBuy); }
    void SetCanTradeImpl(string pair, bool canTrade, bool? isBuy) {
      GetTradingMacro(pair).ForEach(tm => {
        if(!isBuy.HasValue)
          tm.BuySellLevels.ForEach(sr => sr.InManual = canTrade);
        tm.SetCanTrade(canTrade, isBuy);
      });
    }
    [BasicAuthenticationFilter]
    public bool[] ToggleCanTrade(string pair, bool isBuy) {
      return UseTradingMacro(pair, tm => tm.ToggleCanTrade(isBuy)).ToArray();
    }
    [BasicAuthenticationFilter]
    public void SetHedgedPair(string pair, string pairHedge) => UseTraderMacro(pair, tm => {
      if(pair != pairHedge) tm.PairHedge = pairHedge;
    });
    public string ReadHedgedOther(string pair) {
      try {
        return UseTraderMacro(pair, tm => tm.TradingMacroHedged(0).Select(tmh => tmh.Pair)).Concat().SingleOrDefault();
      } catch(Exception exc) {
        throw new Exception(new { pair } + "", exc);
      }
    }
    public void SetCanTrade(string pair, bool canTrade, bool isBuy) {
      GetTradingMacro(pair).ForEach(tm => {
        tm.BuySellLevels.ForEach(sr => sr.InManual = canTrade);
        tm.SetCanTrade(canTrade, isBuy);
      });
    }

    public string[] ReadPairs() {
      var tms = remoteControl?.Value?
        .TradingMacrosCopy;
      var tms2 = tms.Where(tm => tm.IsActive && tm.IsTrader)
        .Select(tm => tm.PairPlain)
        .Concat(new[] { "" })
        .ToArray();
      return tms2;
    }
    private void GetTradingMacro(string pair, Action<TradingMacro> action) {
      GetTradingMacro(pair)
        .ForEach(action);
    }
    private IEnumerable<TradingMacro> GetTradingMacro(string pair, int chartNum) {
      return GetTradingMacros(pair)
        .Skip(chartNum.Min(1))
        .Take(1);
      ;
    }
    private IList<TradingMacro> GetTradingMacro(string pair) {
      return UseTraderMacro(pair);
    }
    private void GetTradingMacros(string pair, Action<TradingMacro> action) => GetTradingMacros(pair).ForEach(action);
    private IEnumerable<T> GetTradingMacros<T>(string pair, Func<TradingMacro, T> map) => GetTradingMacros(pair).Where(tm => tm.IsActive).Select(map);
    private IEnumerable<TradingMacro> GetTradingMacros(string pair) {
      return GetTradingMacros()
        .Where(tm => tm.PairPlain == pair)
        ?? new TradingMacro[0];
    }
    private IEnumerable<TradingMacro> GetTradingMacros() {
      return remoteControl?.Value?
        .TradingMacrosCopy
        .Where(tm => tm.IsActive)
        .OrderBy(tm => tm.TradingGroup)
        .ThenBy(tm => tm.PairIndex)
        .AsEnumerable()
        ?? new TradingMacro[0];
    }

    IEnumerable<TradingMacro> UseTradingMacro(Func<TradingMacro, bool> predicate, string pair, int chartNum) {
      try {
        return GetTradingMacro(pair, chartNum).Where(predicate).Take(1);
      } catch(Exception exc) {
        LogMessage.Send(exc);
        return new TradingMacro[0];
      }
    }

    IEnumerable<T> UseTradingMacroM1<T>(string pair, Func<TradingMacro, T> func) => UseTraderMacro(pair, tm => tm.TradingMacroM1(func)).Concat();

    T UseTradingMacro<T>(string pair, Func<TradingMacro, T> func) {
      return UseTraderMacro(pair, func).First();
    }
    void UseTradingMacro(string pair, Action<TradingMacro> action) {
      UseTraderMacro(pair, action);
    }
    void UseTraderMacro(string pair, Action<TradingMacro> action) {
      UseTradingMacro(pair, tm => tm.IsTrader, action);
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
    TradingMacro[] UseTraderMacro(string pair) => UseTraderMacro(pair, tm => tm);
    T[] UseTraderMacro<T>(string pair, Func<TradingMacro, T> func) {
      return UseTradingMacro2(pair, tm => tm.IsTrader)
        .Count(1, i => Log = new Exception($"{i} Traders found"), i => throw new Exception($"Too many {i} Traders found"), new { pair })
        .Select(func)
        .ToArray();
    }
    TradingMacro[] UseTradingMacro2(string pair, Func<TradingMacro, bool> where) {
      try {
        return GetTradingMacros(pair).Where(where).ToArray();
      } catch(Exception exc) {
        LogMessage.Send(exc);
        return new TradingMacro[0];
      }
    }

    T[] UseTradingMacro2<T>(string pair, Func<TradingMacro, bool> where, Func<TradingMacro, T> func) {
      try {
        return GetTradingMacros(pair).Where(where).Select(func).ToArray();
      } catch(Exception exc) {
        LogMessage.Send(exc);
        return new T[0];
      }
    }
    IEnumerable<T> UseTradingMacro2<T>(string pair, int chartNum, Func<TradingMacro, T> func) {
      try {
        return GetTradingMacro(pair, chartNum).Select(func);
      } catch(Exception exc) {
        LogMessage.Send(exc);
        return new T[0];
      }
    }
  }
}
