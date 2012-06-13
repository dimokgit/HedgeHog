using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.DB;
using HedgeHog.Bars;
using System.Diagnostics;
using Order2GoAddIn;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Reactive;
using HedgeHog.Shared;

namespace HedgeHog.Alice.Store {
  public static class PriceHistory {
    public static void LoadBars(FXCoreWrapper fw,string pairToLoad,Action<object> progressCallback = null) {
      var pairsToLoad = new RequestPairForHistoryMessage();
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<RequestPairForHistoryMessage>(pairsToLoad);
      foreach (var pair in pairsToLoad.Pairs)
        AddTicks(fw, pair.Item2, pair.Item1, DateTime.Now.AddYears(-1), progressCallback);
    }
    static Task saveTicksTask;
    public static void AddTicks(FXCoreWrapper fw, int period, string pair, DateTime dateStart, Action<object> progressCallback) {
      try {
        #region callback
        ActionBlock<Action> saveTickActionBlock = new ActionBlock<Action>(a => a());
        Action<FXCoreWrapper.RateLoadingCallbackArgs<Rate>> showProgress = (args) => {
          SaveTickCallBack(period, pair, progressCallback, saveTickActionBlock, args);
        };
        #endregion

        var offset = TimeSpan.FromMinutes(period);
        using (var context = new ForexEntities()) {
          if (dateStart > DateTime.MinValue) {
            var dateEnd = context.t_Bar.Where(b => b.Pair == pair && b.Period == period).Select(b => b.StartDate).DefaultIfEmpty(DateTime.Now).Min().Subtract(offset).DateTime;
            fw.GetBarsBase<Rate>(pair, period, 0, dateStart, dateEnd, new List<Rate>(), showProgress);
          }
          dateStart = context.t_Bar.Where(b => b.Pair == pair && b.Period == period).Select(b => b.StartDate).DefaultIfEmpty(DateTime.Now).Max().Add(offset).DateTime;
        }
        fw.GetBarsBase<Rate>(pair, period, 0, dateStart, DateTime.Now, new List<Rate>(), showProgress);
      } catch (Exception exc) {
        Debug.WriteLine(exc.ToString());
      }
    }

    public static void SaveTickCallBack(int period, string pair, Action<object> progressCallback, ActionBlock<Action> saveTickActionBlock, FXCoreWrapper.RateLoadingCallbackArgs<Rate> args) {
      if (progressCallback != null) progressCallback(args.Message);
      else Debug.WriteLine("{0}", args.Message);
      var context = new ForexEntities();
      foreach (var t in args.NewRates) {
        var bar = context.CreateObject<t_Bar>();
        FillBar(period, pair, bar, t);
        context.t_Bar.AddObject(bar);
      }
      Action a = new Action(() => {
        try {
          context.SaveChanges(System.Data.Objects.SaveOptions.AcceptAllChangesAfterSave);
          context.Dispose();
        } catch (Exception exc) {
          if (progressCallback != null) progressCallback(exc);
        }
      });
      saveTickActionBlock.Post(a);
    }
    private static void FillBar(int period, string pair, t_Bar bar, Rate t) {
      bar.Pair = pair;
      bar.Period = period;
      bar.StartDate = t.StartDate;
      bar.AskHigh = (float)t.AskHigh;
      bar.AskLow = (float)t.AskLow;
      bar.AskOpen = (float)t.AskOpen;
      bar.AskClose = (float)t.AskClose;
      bar.BidHigh = (float)t.BidHigh;
      bar.BidLow = (float)t.BidLow;
      bar.BidOpen = (float)t.BidOpen;
      bar.BidClose = (float)t.BidClose;
      bar.Volume = t.Volume;
    }
  }
}
