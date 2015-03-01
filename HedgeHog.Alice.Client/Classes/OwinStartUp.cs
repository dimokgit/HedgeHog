using HedgeHog.Alice.Store;
using HedgeHog.Shared;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Cors;
using Microsoft.Owin.FileSystems;
using Microsoft.Owin.StaticFiles;
using Owin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Client {
  public class StartUp {
    public string Pair { get { return "usdjpy"; } }
    double IntOrDouble(double d, double max = 10) {
      return d.Abs() > max ? d.ToInt() : d.Round(1);
    }
    public void Configuration(IAppBuilder app) {

      // Static content
      var fileSystem = new PhysicalFileSystem("./www");
      var fsOptions = new FileServerOptions {
        EnableDirectoryBrowsing = true,
        FileSystem = fileSystem
      };
      app.UseFileServer(fsOptions);

      // SignalR timer
      var remoteControl = App.container.GetExport<RemoteControlModel>();
      
      app.UseCors(CorsOptions.AllowAll);
      app.Use((context, next) => {
        try {
          App.SetSignalRSubjectSubject(() => {
            var tm = remoteControl.Value.TradingMacrosCopy.FirstOrDefault(t => t.PairPlain == Pair);
            if (tm != null) {
              var rl = tm.RateLast;
              if (rl != null) {
                try {
                  var trader = App.container.GetExport<TraderModel>();
                  var hub = GlobalHost.ConnectionManager.GetHubContext<MyHub>();
                  hub.Clients.All.addMessage(new {
                    time = tm.ServerTime.ToString("HH:mm"),
                    prf = IntOrDouble(tm.CurrentGrossInPipTotal, 1),
                    tpm = tm.TicksPerMinuteAverage.ToInt(),
                    dur = TimeSpan.FromMinutes(tm.RatesDuration).ToString(@"hh\:mm"),
                    hgt = tm.RatesHeightInPips.ToInt() + "/" + tm.BuySellHeightInPips.ToInt(),
                    rsdMin = tm.RatesStDevMinInPips
                    //closed = trader.Value.ClosedTrades.OrderByDescending(t=>t.TimeClose).Take(3).Select(t => new { })
                  });
                } catch (Exception exc) {
                  GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
                }
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
        app.MapSignalR();
      } catch (InvalidOperationException exc) {
        if (!exc.Message.StartsWith("Counter"))
          throw;
      }
    }
  }
  public class MyHub : Hub {
    Lazy<RemoteControlModel> remoteControl;
    public MyHub() {
      remoteControl = App.container.GetExport<RemoteControlModel>();
    }
    public MyHub(TradingMacro tm) {
      Clients.All.newInfo(tm.RateLast.PriceAvg + "");
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
    public void MoveTradeLevel(string pair, bool isBuy, int pips) {
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
    public void ToggleStartDate(string pair) {
      UseTradingMacro(pair, tm => tm.ToggleCorridorStartDate());
    }
    public void ToggleIsActive(string pair) {
      UseTradingMacro(pair, tm => tm.ToggleIsActive());
    }
    public void FlipTradeLevels(string pair) {
      UseTradingMacro(pair, tm => tm.FlipTradeLevels());
    }
    public void WrapTradeInCorridor(string pair) {
      UseTradingMacro(pair, tm => tm.WrapTradeInCorridor());
    }
    public void SetDefaultTradeLevels(string pair) {
      UseTradingMacro(pair, tm => tm.SetDefaultTradeLevels());
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
    public void AskRates(int charterWidth,DateTimeOffset startDate,string pair){
      UseTradingMacro(pair, tm =>
      Clients.Caller.addRates(remoteControl.Value.ServeChart(charterWidth, startDate, tm)));
    }

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
          closed  = trader.Value.ClosedTrades.Select(t=>new{})
        });
    }
    public void SetTradeCount(string pair, int tradeCount) {
      GetTradingMacro(pair, tm => tm.SetTradeCount(tradeCount));
    }
    public void StopTrades(string pair) { SetCanTrade(pair, false,null); }
    public void StartTrades(string pair, bool isBuy) { SetCanTrade(pair, true, isBuy); }
    void SetCanTrade(string pair, bool canTrade,bool? isBuy) {
      var tm = GetTradingMacro(pair);
      if (tm != null)
        tm.SetCanTrade(canTrade, isBuy);
    }
    private void GetTradingMacro(string pair,Action<TradingMacro> action) {
      var tm = GetTradingMacro(pair);
      if (tm != null) action(tm);
    }
    private TradingMacro GetTradingMacro(string pair) {
      var rc = remoteControl.Value;
      var tm = rc.TradingMacrosCopy.FirstOrDefault(t => t.PairPlain == pair);
      return tm;
    }
    void UseTradingMacro(string pair,Action<TradingMacro> action) {
      try {
        action(GetTradingMacro(pair));
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
      }
    }

  }
}
