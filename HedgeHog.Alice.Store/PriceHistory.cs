using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.DB;
using HedgeHog.Bars;
using System.Diagnostics;
using Order2GoAddIn;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Store {
  public static class PriceHistory {
    public static void LoadBars(FXCoreWrapper fw,string pairToLoad,Action<object> progressCallback = null) {
      var pairsAvl = fw.CoreFX.Instruments;
      var pairs = new ForexEntities().v_Pair.Select(p => p.Pair).ToArray();
      if (!string.IsNullOrWhiteSpace(pairToLoad)) pairs = pairs.Where(p => p == pairToLoad).ToArray();
      foreach (var minutes in new[] { 15, 30, 60 })
        foreach (var pair in pairs) {
          fw.CoreFX.SetOfferSubscription(pair);
          AddTicks(fw,minutes, pair, DateTime.Now.AddYears(-4),progressCallback);
        }
    }
    static Task saveTicksTask;
    public static void AddTicks(FXCoreWrapper fw, int period, string pair, DateTime dateStart, Action<object> progressCallback) {
      try {
        #region callback
        Action<FXCoreWrapper.RateLoadingCallbackArgs<Rate>> showProgress = (args) => {
          if (progressCallback != null) progressCallback(args.Message);
          else Debug.WriteLine("{0}", args.Message);
          var context = new ForexEntities();
          foreach (var t in args.NewRates) {
            var bar = context.CreateObject<t_Bar>();
            FillBar(period, pair, bar, t);
            context.t_Bar.AddObject(bar);
          }
          if (saveTicksTask != null)
            Task.WaitAll(saveTicksTask);
          Action a = new Action(() => {
            try {
              context.SaveChanges(System.Data.Objects.SaveOptions.AcceptAllChangesAfterSave);
              context.Dispose();
            } catch (Exception exc) {
              if (progressCallback != null) progressCallback(exc);
            }
          });
          saveTicksTask = Task.Factory.StartNew(a);
        };
        #endregion

        var offset = TimeSpan.FromMinutes(period);
        using (var context = new ForexEntities()) {
          if (dateStart > DateTime.MinValue) {
            var dateEnd = context.t_Bar.Where(b => b.Pair == pair && b.Period == period).Select(b => b.StartDate).DefaultIfEmpty(DateTime.Now).Min().Subtract(offset);
            fw.GetBarsBase(pair, period, 0, dateStart, dateEnd, new List<Rate>(), showProgress);
          }
          dateStart = context.t_Bar.Where(b => b.Pair == pair && b.Period == period).Select(b => b.StartDate).DefaultIfEmpty(DateTime.Now).Max().Add(offset);
        }
        fw.GetBarsBase(pair, period, 0, dateStart, DateTime.Now, new List<Rate>(), showProgress);
      } catch (Exception exc) {
        Debug.WriteLine(exc.ToString());
      }
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
    }
  }
}
