using HedgeHog.Bars;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    private CorridorStatistics ScanCorridorLazy(IList<Rate> ratesReversed, Func<IList<Rate>, int> counter, Func<CorridorStatistics> showVolts = null) {
      IEnumerable<IList<Rate>> ratesCount = CorridorStats.Rates.CopyLast(1)
        .Where(_ => IsCorridorForwardOnly)
        .Select(rl => ratesReversed.TakeWhile(r => r.StartDate >= rl.StartDate)
          .Count())
          .Select(c => (c * 1.05).Min(RatesArray.Count).ToInt())
          .Select(c => ratesReversed.Take(c).ToArray());
      var rates = ratesCount.DefaultIfEmpty(ratesReversed).First();
      return ScanCorridorLazy(rates, new Lazy<int>(() => counter(rates)), showVolts);
    }

    class TaskRunner {
      Task _task;
      Action _action;
      public void Run(Action a) {
        if(_task != null && !_task.IsCompleted)
          _action = a;
        else {
          _action = null;
          _task = Task.Run(a);
          _task.ContinueWith(_ => {
            if(_action != null)
              Run(_action);
          });
        }
      }
    }
    TaskRunner _corridorsTask = new TaskRunner();
    private CorridorStatistics ScanCorridorLazy(IList<Rate> ratesReversed, Lazy<int> lazyCount, Func<CorridorStatistics> showVolts = null, Action postProcess = null) {
      Lazy<int> lenghForwardOnly = new Lazy<int>(() => {
        if(ratesReversed.Count < RatesArray.Count)
          return ratesReversed.Count;
        var date = CorridorStats.Rates.LastOrDefault()?.StartDate;
        return ratesReversed.TakeWhile(r => r.StartDate >= date).Count();
      });
      var lengthMax = new Lazy<int>(() => !IsCorridorForwardOnly || CorridorStats.StartDate.IsMin() ? int.MaxValue : lenghForwardOnly.Value);

      var startMax = new Lazy<DateTime>(() => CorridorStopDate.IfMin(DateTime.MaxValue));
      var startMin = CorridorStartDate.GetValueOrDefault(CorridorStats.StartDate);
      var rates = !CorridorStartDate.HasValue
        ? !CorridorStopDate.IsMin()
        ? ratesReversed
          .SkipWhile(r => lazyCount.Value > 0 && r.StartDate > startMax.Value)
          .TakeWhile(r => r.StartDate > ratesReversed[lazyCount.Value].StartDate).ToArray()
        : ratesReversed.SkipWhile(r => lazyCount.Value > 0 && r.StartDate > startMax.Value).Take(lengthMax.Value.Min(lazyCount.Value)).ToArray()
        : ratesReversed.SkipWhile(r => r.StartDate > startMax.Value).TakeWhile(r => r.StartDate >= startMin).ToArray();
      if(IsCorridorForwardOnly && _isCorridorStopDateManual && rates.Last().StartDate < CorridorStats.StartDate)
        WaveShort.ResetRates(CorridorStats.Rates);
      else
        WaveShort.ResetRates(rates);
      if(CorridorStartDate.HasValue)
        _corridorsTask.Run(() => {
          CorridorLengthGreen = ratesReversed.TakeWhile(r => r.StartDate >= _corridorStartDate1).Count();
          CorridorLengthBlue = ratesReversed.TakeWhile(r => r.StartDate >= _corridorStartDate2).Count();
          throw new Exception("Obsolete code entered");
        });
      postProcess?.Invoke();
      return (showVolts ?? GetShowVoltageFunction())();
    }
    IList<Rate> TryGetTrendLines(Func<IList<Rate>> calc, IList<Rate> defaultList) {
      try {
        return calc();
      } catch(Exception exc) {
        Log = exc;
        return defaultList;
      }
    }
    private CorridorStatistics ShowVoltsNone() {
      return null;
      if(!WaveShort.HasRates)
        WaveShort.Rates = RatesArray.ToArray().Reverse().Take(CorridorDistance).ToArray();
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }


    private CorridorStatistics ShowVolts(double volt, Func<Rate, double> getVolt = null, Action<Rate, double> setVolt = null) {
      return ShowVolts(volt, VoltAverageIterations, getVolt, setVolt);
    }
    private CorridorStatistics ShowVolts(double volt, int averageIterations, Func<Rate, double> getVolt = null, Action<Rate, double> setVolt = null) {
      SetVolts(volt, getVolt ?? GetVoltage, setVolt ?? SetVoltage, averageIterations);
      if(!WaveShort.HasRates)
        return null;
      var corridor = WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
      return corridor;
    }

    private void SetVolts(double volt, bool cmaByRates) {
      SetVots(volt, cmaByRates ? CmaPeriodByRatesCount() : 0);
    }
    private void SetVots(double volt, double cma = 0) {
      SetVolts(volt, GetVoltage, SetVoltage, cma);
    }

    //    SetVoltHighByIndex(voltIndex)(min.Abs());
    //        SetVoltLowByIndex(voltIndex)(-min.Abs());

    private void SetVolts(double volt, Func<Rate, double> getVolt, Action<Rate, double> setVolt, double cma = 0) {
      if(!IsRatesLengthStable)
        return;
      if(double.IsInfinity(volt) || double.IsNaN(volt))
        return;
      var volt2 = cma > 0 ? GetLastVolt(getVolt).Select(v => v.Cma(cma, volt)).DefaultIfEmpty(volt).SingleOrDefault() : volt;
      UseRates(rates => rates.Where(r => getVolt(r).IsNaN()).ToList())
        .SelectMany(rates => rates).ForEach(r => setVolt(r, volt2));
      //SetVoltage(RateLast, volt);
      if(getVolt != GetVoltage)
        return;
      var voltRates = RatesArray.Select(getVolt).SkipWhile(v => v.IsNaN()).ToArray();
      if(voltRates.Any()) {
        GeneralPurposeSubject.OnNext(() => {
          try {
            var voltageAvgLow = voltRates.AverageByIterations(-VoltAverageIterations).DefaultIfEmpty(double.NaN).Average();
            GetVoltageAverage = () => new[] { voltageAvgLow };
            var voltageAvgHigh = voltRates.AverageByIterations(VoltAverageIterations).DefaultIfEmpty(double.NaN).Average();
            GetVoltageHigh = () => new[] { voltageAvgHigh };
          } catch(Exception exc) { Log = exc; }
        });
      }
    }
    private void SetVolts(double volt, int voltIndex) {
      if(!IsRatesLengthStable)
        return;
      if(double.IsInfinity(volt) || double.IsNaN(volt))
        return;
      UseRates(rates => rates.BackwardsIterator().TakeWhile(r => GetVoltByIndex(voltIndex)(r).IsNaN())
        .ForEach(r => SetVoltByIndex(voltIndex)(r, volt)));
      //SetVoltage(RateLast, volt);
      var voltRates = RatesArray.Select(GetVoltByIndex(voltIndex)).SkipWhile(v => v.IsNaN())
        .Scan((p, n) => n.IsNaN() ? p : n)
        .ToArray();
      if(voltRates.Any()) {
        GeneralPurposeSubject.OnNext(() => {
          try {
            var voltageAvgLow = voltRates.AverageByIterations(-VoltAverageIterationsByIndex(voltIndex)).DefaultIfEmpty(double.NaN).Average();
            SetVoltLowByIndex(voltIndex)(voltageAvgLow);
            var voltageAvgHigh = voltRates.AverageByIterations(VoltAverageIterationsByIndex(voltIndex)).DefaultIfEmpty(double.NaN).Average();
            SetVoltHighByIndex(voltIndex)(voltageAvgHigh);
          } catch(Exception exc) { Log = exc; }
        });
      }
    }
    private void SetVoltsM1() { SetVoltsM1(GetVoltage, tm => tm.GetVoltage, tm => tm.SetVoltage); }
    private void SetVoltsM1_2() { SetVoltsM1(GetVoltage2, tm => tm.GetVoltage2, tm => tm.SetVoltage2); }
    private void SetVoltsM1(
      Func<Rate, double> getVolt,
      Func<TradingMacro, Func<Rate, double>> getVoltM1,
      Func<TradingMacro, Action<Rate, double>> setVoltM1) {
      UseRates(rates => rates.BackwardsIterator().TakeWhile(r => getVolt(r).IsNaN()).ToList()).ForEach(ratesEmpty => {
        ratesEmpty.Reverse();
        ratesEmpty.Select(r => r.StartDate).Take(1).ForEach(startDate => {
          var tm = TradingMacroOther().First(t => t.IsTrader);
          tm.UseRates(rates => rates.BackwardsIterator().TakeWhile(r => getVoltM1(tm)(r).IsNotNaN() && r.StartDate >= startDate).ToList()).ForEach(ratesT1 => {
            ratesT1.Reverse();
            (from r in ratesT1
             group r by r.StartDate.Round() into gr
             select new { d = gr.Key, v = gr.Select(getVoltM1(tm)).Average() } into dv
             join rateEmpty in ratesEmpty on dv.d equals rateEmpty.StartDate
             select new { r = rateEmpty, dv.v }
            ).ForEach(x => setVoltM1(tm)(x.r, x.v));
          });
        });
      });
    }
    static Subject<TradingMacro> SyncStraddleHistorySubject = new Subject<TradingMacro>();
    void SyncStraddleHistoryM1(TradingMacro tm) {
      var zip = (from shs in UseStraddleHistory(staddleHistory =>
         (from sh in StraddleHistory
          group sh by sh.time.Round() into g
          orderby g.Key
          select new { bid = g.Average(t => t.bid), time = g.Key.ToLocalTime() }
          ).ToList())
                 let endDate = shs.Last().time.ToLocalTime()
                 from z in tm.UseRates(ra => ra.TakeWhile(r => r.StartDate <= endDate).Zip(r => r.StartDate, shs, sh => sh.time, (r, sh) => (r, sh)).ToArray())
                 from t in z
                 select t
                 ).ToArray();
      zip.ForEach(t => SetVoltage(t.r, t.sh.bid));
    }
    void SyncStraddleHistoryT1(TradingMacro tm) {
      var zip = (from shs in UseStraddleHistory(staddleHistory =>
         (from sh in StraddleHistory
          orderby sh.time
          select new { bid = sh.bid, time = sh.time.ToLocalTime() }
          ).ToList())
                 let endDate = shs.Last().time.ToLocalTime()
                 from z in tm.UseRates(ra => ra.TakeWhile(r => r.StartDate <= endDate).Zip(r => r.StartDate, shs, sh => sh.time, (r, sh) => (r, sh)).ToArray())
                 from t in z
                 select t
                 ).ToArray();
      zip.ForEach(t => SetVoltage(t.r, t.sh.bid));
    }
    private void SetVoltsByPpm() {
      SetVots(WaveRangeAvg.PipsPerMinute, 2);
    }
    private CorridorStatistics ShowVoltsByBPA1() {
      SetVots(_wwwBpa1, 2);
      return null;
    }
  }
}
