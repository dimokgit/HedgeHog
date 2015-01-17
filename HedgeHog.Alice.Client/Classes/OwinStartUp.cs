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
                    duration = tm.RatesDuration,
                    count = tm.BarsCountCalc,
                    height = tm.RatesHeightInPips.ToInt() + "/" + tm.BuySellHeightInPips.ToInt(),
                    stDev = IntOrDouble(tm.StDevByHeightInPips,1) + "/" + IntOrDouble(tm.StDevByPriceAvgInPips,1)
                    //profit = IntOrDouble(tm.CurrentGrossInPips),
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
        } catch (Exception exc) {
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        }
        return next();
      });
      // SignalR
      app.MapSignalR();
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
    double IntOrDouble(double d, double max = 10) {
      return d.Abs() > max ? d.ToInt() : d.Round(1);
    }
    public void ResetPlotter(string pair) {
      var tm = GetTradingMacro(pair);
      var trader = App.container.GetExport<TraderModel>();
      if (tm != null)
        Clients.All.resetPlotter(new {
          time = tm.ServerTime.ToString("HH:mm:ss"),
          duration = tm.RatesDuration,
          count = tm.BarsCountCalc,
          stDev = IntOrDouble(tm.StDevByHeightInPips) + "/" + IntOrDouble(tm.StDevByPriceAvgInPips),
          height = tm.RatesHeightInPips.ToInt(),
          profit = IntOrDouble(tm.CurrentGrossInPips),
          closed  = trader.Value.ClosedTrades.Select(t=>new{})
        });
    }
    public void StopTrades(string pair) { SetCanTrade(pair, false); }
    public void StartTrades(string pair) { SetCanTrade(pair, true); }
    void SetCanTrade(string pair, bool canTrade) {
      var tm = GetTradingMacro(pair);
      if (tm != null)
        tm.SetCanTrade(canTrade);
    }
    private TradingMacro GetTradingMacro(string pair) {
      var rc = remoteControl.Value;
      var tm = rc.TradingMacrosCopy.FirstOrDefault(t => t.PairPlain == pair);
      return tm;
    }
  }
}
