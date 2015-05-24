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
  public sealed class GZipMiddleware {
    private readonly Func<IDictionary<string, object>, Task> next;

    public GZipMiddleware(Func<IDictionary<string, object>, Task> next) {
      this.next = next;
    }

    public async Task Invoke(IDictionary<string, object> environment) {
      var context = new OwinContext(environment);

      // Verifies that the calling client supports gzip encoding.
      if (!(from encoding in context.Request.Headers.GetValues("Accept-Encoding") ?? Enumerable.Empty<string>()
            from enc in encoding.Split(',').Select(s=>s.Trim())
            where String.Equals(enc, "gzip", StringComparison.OrdinalIgnoreCase)
            select encoding).Any()) {
        await next(environment);
        return;
      }
      context.Response.Headers.Add("Content-Encoding", new[] { "gzip" });

      // Replaces the response stream by a delegating memory
      // stream and keeps track of the real response stream.
      var body = context.Response.Body;
      context.Response.Body = new BufferingStream(context, body);

      try {
        await next(environment);

        if (context.Response.Body is BufferingStream) {
          await context.Response.Body.FlushAsync();
        }
      } finally {
        // Restores the real stream in the environment dictionary.
        context.Response.Body = body;
      }
    }

    private sealed class BufferingStream : MemoryStream {
      private readonly IOwinContext context;
      private Stream stream;

      internal BufferingStream(IOwinContext context, Stream stream) {
        this.context = context;
        this.stream = stream;
      }

      public override async Task FlushAsync(CancellationToken cancellationToken) {
        // Determines if the memory stream should
        // be copied in the response stream.
        if (!(stream is GZipStream)) {
          Seek(0, SeekOrigin.Begin);
          await CopyToAsync(stream, 8192, cancellationToken);
          SetLength(0);

          return;
        }

        // Disposes the GZip stream to allow
        // the footer to be correctly written.
        stream.Dispose();
      }

      public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
        // Determines if the stream has already been replaced by a GZip stream.
        if (stream is GZipStream) {
          // The delegated stream is already a GZip stream, continue streaming.
          await stream.WriteAsync(buffer, offset, count, context.Request.CallCancelled);
          return;
        }

        // Determines if the memory stream should continue buffering.
        if ((count + Length) < 4096) {
          // Continues buffering.
          await base.WriteAsync(buffer, offset, count, cancellationToken);
          return;
        }

        // Determines if chunking can be safely used.
        if (!String.Equals(context.Request.Protocol, "HTTP/1.1", StringComparison.Ordinal)) {
          throw new InvalidOperationException("The Transfer-Encoding: chunked mode can only be used with HTTP/1.1");
        }

        // Sets the appropriate headers before changing the response stream.
        context.Response.Headers["Content-Encoding"] = "gzip";
        context.Response.Headers["Transfer-Encoding"] = "chunked";

        // Writes the buffer in the memory stream.
        await base.WriteAsync(buffer, offset, count, cancellationToken);

        // Opens a new GZip stream pointing directly to the real response stream.
        stream = new GZipStream(stream, CompressionMode.Compress, leaveOpen: true);

        // Rewinds the delegating memory stream
        // and copies it to the GZip stream.
        Seek(0, SeekOrigin.Begin);
        await CopyToAsync(stream, 8192, context.Request.CallCancelled);
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
        otg = IntOrDouble(tm.OpenTradesGross2InPips, 1),
        tps = tm.TicksPerSecondAverage.Round(1),
        dur = TimeSpan.FromMinutes(tm.RatesDuration).ToString(@"hh\:mm"),
        hgt = tm.RatesHeightInPips.ToInt() + "/" + tm.BuySellHeightInPips.ToInt(),
        rsdMin = tm.RatesStDevMinInPips,
        equity = remoteControl.Value.MasterModel.AccountModel.Equity.Round(0),
        price = new { ask = tm.CurrentPrice.Ask, bid = tm.CurrentPrice.Bid },
        tci = GetTradeConditionsInfo(tm),
        wfs = tm.WorkflowStep
        //closed = trader.Value.ClosedTrades.OrderByDescending(t=>t.TimeClose).Take(3).Select(t => new { })
      });
      UseTradingMacro(pair, tm => Clients.Caller.addMessage(makeClienInfo(tm)));
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
      UseTradingMacro(pair, chartNumber, tm => tm.ToggleCorridorStartDate());
    }
    public void ToggleIsActive(string pair, int chartNumber) {
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
    public void Buy(string pair) {
      UseTradingMacro(pair, tm => tm.OpenTrade(true, tm.LotSizeByLossBuy, "web"));
    }
    public void Sell(string pair) {
      UseTradingMacro(pair, tm => tm.OpenTrade(false, tm.LotSizeByLossBuy, "web"));
    }
    public void SetRsdTreshold(string pair, int pips) {
      UseTradingMacro(pair, tm => tm.RatesStDevMinInPips = pips);
    }
    public object[] AskRates(int charterWidth, DateTimeOffset startDate, DateTimeOffset endDate, string pair,int chartNum) {
      return UseTradingMacro2(pair, chartNum, tm => tm.IsActive
        , tm => remoteControl.Value.ServeChart(charterWidth, startDate, endDate, tm));
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
      });
    }

    static SendChartBuffer _sendChart2Buffer = SendChartBuffer.Create();
    public object[] AskRates2(int charterWidth, DateTimeOffset startDate, DateTimeOffset endDate, string pair) {
      return UseTradingMacro2(pair, 1, tm => tm.IsActive
        , tm => remoteControl.Value.ServeChart(charterWidth, startDate, endDate, tm));
    }
    public void SetPresetTradeLevels(string pair, TradeLevelsPreset presetLevels, object isBuy) {
      bool? b = isBuy == null ? null : (bool?)isBuy;
      UseTradingMacro(pair, tm => tm.SetTradeLevelsPreset(presetLevels, b));
    }
    public void SetTradeLevel(string pair, bool isBuy, int level) {
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
      var rc = remoteControl.Value;
      return rc.TradingMacrosCopy
        .Skip(chartNum)
        .Take(1)
        .Where(tm2 => tm2.IsActive)
        .Where(t => t.PairPlain == pair);
    }
    T UseTradingMacro<T>(string pair, Func<TradingMacro, T> func) {
      return UseTradingMacro(pair, 0, func);
    }
    void UseTradingMacro(string pair, Action<TradingMacro> action) {
      UseTradingMacro(pair, 0, action);
    }
    void UseTradingMacro(string pair, int chartNum, Action<TradingMacro> action) {
      try {
        GetTradingMacro(pair, chartNum).ForEach(action);
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
      }
    }
    T UseTradingMacro<T>(string pair, int chartNum, Func<TradingMacro, T> func) {
      try {
        return GetTradingMacro(pair, chartNum).Select(func).DefaultIfEmpty(default(T)).First();
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }
    T[] UseTradingMacro2<T>(string pair, int chartNum, Func<TradingMacro, bool> where, Func<TradingMacro, T> func) {
      try {
        return GetTradingMacro(pair, chartNum).Where(where).Select(func).ToArray();
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }
    void UseTradingMacro2(string pair, int chartNum, Action<TradingMacro> func) {
      try {
        GetTradingMacro(pair, chartNum).ForEach(func);
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        throw;
      }
    }

  }
}
