using HedgeHog.Bars;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    private CorridorStatistics ScanCorridorLazy(IList<Rate> ratesReversed, Func<IList<Rate>, int> counter, Func<CorridorStatistics> showVolts = null) {
      return ScanCorridorLazy(ratesReversed, new Lazy<int>(() => counter(ratesReversed)), showVolts);
    }
    private CorridorStatistics ScanCorridorLazy(IList<Rate> ratesReversed, Lazy<int> lazyCount, Func<CorridorStatistics> showVolts = null, Action postProcess = null) {

      Lazy<int> lenghForwardOnly = new Lazy<int>(() => {
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
      if (postProcess != null) postProcess();
      return (showVolts ?? ShowVoltsNone)();
    }
    private CorridorStatistics ShowVoltsNone() {
      if (!WaveShort.HasRates)
        WaveShort.Rates = RatesArray.TakeLast(CorridorDistance).Reverse().ToArray();
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
      var t1 = Task.Factory.StartNew(() => {
        try {
          var voltageAvgLow = voltRates.AverageByIterations(-averageIterations).Average();
          GetVoltageAverage = () => voltageAvgLow;
        } catch (Exception exc) { Log = exc; }
      });
      var t2 = Task.Factory.StartNew(() => {
        try {
          var voltageAvgHigh = voltRates.AverageByIterations(averageIterations).Average();
          GetVoltageHigh = () => voltageAvgHigh;
        } catch (Exception exc) { Log = exc; }
      });
      var t3 = Task.Factory.StartNew(() => {
        CorridorCorrelation = AlgLib.correlation.spearmanrankcorrelation(RatesArray.Select(_priceAvg).ToArray(), RatesArray.Select(GetVoltage).ToArray(), RatesArray.Count);
      });
      var corridor = WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
      Task.WaitAll(t1, t2, t3);
      return corridor;
    }
  }
}
