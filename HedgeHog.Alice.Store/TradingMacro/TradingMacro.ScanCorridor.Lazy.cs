using HedgeHog.Bars;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
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
        var date = CorridorStats.Rates.Last().StartDate;
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
        });
      if(postProcess != null)
        postProcess();
      if(WaveShort.HasRates) {
        TrendLines1 = Lazy.Create(() => CalcTrendLines(CorridorLengthGreen), TrendLines1.Value, exc => Log = exc);
        var trendRates = WaveShort.Rates.Reverse().ToList();
        TrendLines = Lazy.Create(() => CalcTrendLines(trendRates), TrendLines.Value, exc => Log = exc);
        TrendLines2 = Lazy.Create(() => CalcTrendLines(CorridorLengthBlue), TrendLines2.Value, exc => Log = exc);
      }
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
      if(!WaveShort.HasRates)
        WaveShort.Rates = RatesArray.ToArray().Reverse().Take(CorridorDistance).ToArray();
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

    private CorridorStatistics ShowVolts_Slow(double volt, int averageIterations) {
      RatesArray.Where(r => GetVoltage(r).IsNaN()).ToList().ForEach(r => SetVoltage(r, volt));
      //SetVoltage(RateLast, volt);
      var voltageAvgLow = RatesArray.Select(GetVoltage).SkipWhile(v => v.IsNaN()).ToArray().AverageByIterations(-averageIterations).Average();
      GetVoltageAverage = () => voltageAvgLow;
      var voltageAvgHigh = RatesArray.Select(GetVoltage).SkipWhile(v => v.IsNaN()).ToArray().AverageByIterations(averageIterations).Average();
      GetVoltageHigh = () => voltageAvgHigh;
      CorridorCorrelation = AlgLib.correlation.spearmanrankcorrelation(RatesArray.Select(_priceAvg).ToArray(), RatesArray.Select(GetVoltage).ToArray(), RatesArray.Count);
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

    private CorridorStatistics ShowVolts(double volt, int averageIterations) {
      SetVots(volt, averageIterations);
      if(!WaveShort.HasRates)
        return null;
      var corridor = WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
      return corridor;
    }

    private void SetVots(double volt, int averageIterations, bool cmaByRates) {
      SetVots(volt, averageIterations, cmaByRates ? CmaPeriodByRatesCount() : 0);
    }
    private void SetVots(double volt, int averageIterations, double cma = 0) {
      if(!WaveShort.HasRates)
        return;
      var volt2 = cma > 0 ? GetLastVolt().Select(v => v.Cma(cma, volt)).SingleOrDefault() : volt;
      UseRates(rates => rates.Where(r => GetVoltage(r).IsNaN()).ToList())
        .SelectMany(rates => rates).ForEach(r => SetVoltage(r, volt2));
      //SetVoltage(RateLast, volt);
      var voltRates = RatesArray.Select(GetVoltage).SkipWhile(v => v.IsNaN()).ToArray();
      if(voltRates.Any()) {
        GeneralPurposeSubject.OnNext(() => {
          try {
            var voltageAvgLow = voltRates.AverageByIterations(-averageIterations).DefaultIfEmpty(double.NaN).Average();
            GetVoltageAverage = () => voltageAvgLow;
            var voltageAvgHigh = voltRates.AverageByIterations(averageIterations).DefaultIfEmpty(double.NaN).Average();
            GetVoltageHigh = () => voltageAvgHigh;
          } catch(Exception exc) { Log = exc; }
        });
      }
    }
    private void SetVoltsM1() {
      UseRates(rates => rates.BackwardsIterator().TakeWhile(r => GetVoltage(r).IsNaN()).ToList()).ForEach(ratesEmpty => {
        ratesEmpty.Reverse();
        ratesEmpty.Select(r => r.StartDate).Take(1).ForEach(startDate => {
          var tm = TradingMacroOther().First(t => t.IsTrader);
          tm.UseRates(rates => rates.BackwardsIterator().TakeWhile(r => GetVoltage(r).IsNotNaN() && r.StartDate >= startDate).ToList()).ForEach(ratesT1 => {
            ratesT1.Reverse();
            (from r in ratesT1
             group r by r.StartDate.Round() into gr
             select new { d = gr.Key, v = gr.Select(tm.GetVoltage).Average() } into dv
             join rateEmpty in ratesEmpty on dv.d equals rateEmpty.StartDate
             select new { r = rateEmpty, v = dv.v }
            ).ForEach(x => tm.SetVoltage(x.r, x.v));
          });
        });
      });
    }
    private void SetVoltsByPpm() {
      SetVots(WaveRangeAvg.PipsPerMinute, 2);
    }
    private void SetVoltsByEquinox() {
      if(IsRatesLengthStable)
        EquinoxValuesImpl2(EquinoxTrendLines)
          .Take(1)
          .ForEach(t => SetVots(t.Item2.Item1, 2));
    }
    private void SetVoltsByPpmRatio() {
      SetVots(WaveRangeSum.PipsPerMinute / WaveRangeAvg.PipsPerMinute, 2);
    }
    private CorridorStatistics SetVoltsByBPA1() {
      SetVots(_wwwBpa1, 2);
      return null;
    }
  }
}
