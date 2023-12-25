using HedgeHog;
using HedgeHog.Bars;
using HedgeHog.Core;
using HedgeHog.Shared;
using IBApi;
using IBApp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using static ConsoleApp.Program;

namespace ConsoleApp {
  static partial class LoadTest {
    public static void LoadHistory(this IBClientCore ibClient, Contract c) {
      var dateEnd = DateTime.Now;// new DateTime(DateTime.Parse("2017-06-21 12:00").Ticks, DateTimeKind.Local);
      DataMapDelegate<Rate> map = (DateTime date, double open, double high, double low, double close, long volume, int count) => new Rate(date, high, low, true);
      var counter = 0;
      var sw = Stopwatch.StartNew();
      HandleMessage($"Loading History for {c}");
      new HistoryLoader_Slow<Rate>(ibClient, c, 4500, dateEnd, TimeSpan.FromDays(100000), TimeUnit.W, BarSize._15_mins, false,
         map,
         list => {
           HandleMessage($"{c} {new { list = new { list.Count, first = list.First().StartDate, last = list.Max(r => r.StartDate), sw.Elapsed, Thread = Thread.CurrentThread.ManagedThreadId } }}");
           var pivot = (from b in list
                        group b by b.StartDate.Date into g
                        select new { date = g.Key, count = g.Count() }
                        );
           //HandleMessage(pivot.ToTextOrTable($"Rates Pivot:{list.Count}"));
           //return;
           var mul = c.ComboMultiplier;
           var cmas = list.Select(b => b.PriceAvg*mul).Cma(5);
           var csv = list.Zip(cmas, (b, cma) => new { b.StartDate, cma });
           Thread thread = new Thread(() => Clipboard.SetText(csv.Csv("{0},{1}", b => 
            b.StartDate.ToString("MM/dd/yyyy HH:mm"), b=>b.cma)));
           thread.SetApartmentState(ApartmentState.STA);
           thread.Start();
           thread.Join();
           HandleMessage("Check clipboard");
         },
         dates => {
           HandleMessage($"{c} {new { dateStart = dates.FirstOrDefault().StartDate.ToTWSDateString(), dateEnd = dates.LastOrDefault().StartDate.ToTWSDateString(), reqCount = ++counter, dates.Count, Thread = Thread.CurrentThread.ManagedThreadId }}");
           HandleMessage(dates.Select(r => new { r.StartDate, r.PriceAvg }).OrderByDescending(x => x.StartDate).SafeIList().FirstAndLast().ToTextOrTable("Rates:"));
         },
         exc => {
           if(!(exc is SoftException))
             ExceptionDispatchInfo.Capture(exc).Throw();
         });
    }

    public static void LoadHistory(this IBClientCore ibClient, IList<Contract> options) {
      var dateEnd = DateTime.Now;// new DateTime(DateTime.Parse("2017-06-21 12:00").Ticks, DateTimeKind.Local);
      DataMapDelegate<Rate> map = (DateTime date, double open, double high, double low, double close, long volume, int count) => new Rate(date, high, low, true);
      var counter = 0;
      if(options.Any()) {
        var c = options[0];
        var sw = Stopwatch.StartNew();
        HandleMessage($"Loading History for {c}");
        new HistoryLoader_Slow<Rate>(ibClient, c, 4500, dateEnd, TimeSpan.FromDays(100000), TimeUnit.W, BarSize._1_hour, false,
           map,
           list => {
             HandleMessage($"{c} {new { list = new { list.Count, first = list.First().StartDate, last = list.Max(r => r.StartDate), sw.Elapsed, Thread = Thread.CurrentThread.ManagedThreadId } }}");
             var pivot = (from b in list
                          group b by b.StartDate.Date into g
                          select new { date = g.Key, count = g.Count() }
                          ).ToTextOrTable($"Rates Pivot:{list.Count}");
             HandleMessage(pivot);
             return;
             Thread thread = new Thread(() => Clipboard.SetText(list.Csv()));
             thread.SetApartmentState(ApartmentState.STA);
             thread.Start();
             thread.Join();
           },
           dates => {
             HandleMessage($"{c} {new { dateStart = dates.FirstOrDefault().StartDate.ToTWSDateString(), dateEnd = dates.LastOrDefault().StartDate.ToTWSDateString(), reqCount = ++counter, dates.Count, Thread = Thread.CurrentThread.ManagedThreadId }}");
             HandleMessage(dates.Select(r => new { r.StartDate, r.PriceAvg }).OrderByDescending(x => x.StartDate).SafeIList().FirstAndLast().ToTextOrTable("Rates:"));
           },
           exc => {
             if(!(exc is SoftException))
               ExceptionDispatchInfo.Capture(exc).Throw();
           });
      } else
        HandleMessage(new { options = options.ToJson() });
    }
  }
}
