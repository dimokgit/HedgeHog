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
        if (_task != null && !_task.IsCompleted)
          _action = a;
        else {
          _action = null;
          _task = Task.Run(a);
          _task.ContinueWith(_ => {
            if (_action != null) Run(_action);
          });
        }
      }
    }
    TaskRunner _corridorsTask = new TaskRunner();
    private CorridorStatistics ScanCorridorLazy(IList<Rate> ratesReversed, Lazy<int> lazyCount, Func<CorridorStatistics> showVolts = null, Action postProcess = null) {
      Lazy<int> lenghForwardOnly = new Lazy<int>(() => {
        if (ratesReversed.Count < RatesArray.Count) return ratesReversed.Count;
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
      if (IsCorridorForwardOnly && _isCorridorStopDateManual && rates.Last().StartDate < CorridorStats.StartDate)
        WaveShort.ResetRates(CorridorStats.Rates);
      else WaveShort.ResetRates(rates);
      if (CorridorStartDate.HasValue)
        _corridorsTask.Run(() => {
          _corridorLength1 = ratesReversed.TakeWhile(r => r.StartDate >= _corridorStartDate1).Count();
          _corridorLength2 = ratesReversed.TakeWhile(r => r.StartDate >= _corridorStartDate2).Count();
        });
      if (postProcess != null) postProcess();
      TrendLines1 = Lazy.Create(() => CalcTrendLines(_corridorLength1), TrendLines1.Value, exc => Log = exc);
      TrendLines = Lazy.Create(SetTrendLines1231, TrendLines.Value, exc => Log = exc);
      TrendLines2 = Lazy.Create(() => CalcTrendLines(_corridorLength2), TrendLines2.Value, exc => Log = exc);
      return (showVolts ?? GetShowVoltageFunction())();
    }
    IList<Rate> TryGetTrendLines(Func<IList<Rate>> calc,IList<Rate> defaultList) {
      try {
        return calc();
      } catch (Exception exc) {
        Log = exc;
        return defaultList;
      }
    }
    private CorridorStatistics ShowVoltsNone() {
      if (!WaveShort.HasRates)
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
      if (!WaveShort.HasRates) return null;
      RatesArray.Where(r => GetVoltage(r).IsNaN()).ToList().ForEach(r => SetVoltage(r, volt));
      //SetVoltage(RateLast, volt);
      var voltRates = RatesArray.Select(GetVoltage).SkipWhile(v => v.IsNaN()).ToArray();
      var tasks = new List<Task>();
      if (voltRates.Any()) {
        tasks.Add(Task.Factory.StartNew(() => {
          try {
            var voltageAvgLow = voltRates.AverageByIterations(-averageIterations).DefaultIfEmpty(double.NaN).Average();
            GetVoltageAverage = () => voltageAvgLow;
          } catch (Exception exc) { Log = exc; }
        }));
        tasks.Add(Task.Factory.StartNew(() => {
          try {
            var voltageAvgHigh = voltRates.AverageByIterations(averageIterations).DefaultIfEmpty(double.NaN).Average();
            GetVoltageHigh = () => voltageAvgHigh;
          } catch (Exception exc) { Log = exc; }
        }));
      }
      var corridor = WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
      Task.WaitAll(tasks.ToArray());
      return corridor;
    }
  }
}
