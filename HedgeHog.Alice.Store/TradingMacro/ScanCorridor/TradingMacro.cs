using HedgeHog.Bars;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    //ScanCorridorByFtfLinearRegressionCorrelation
    private CorridorStatistics ScanCorridorByFftMA(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = UseRatesInternal(ri => ri.Reverse().ToArray());
      SetVoltsByStDevDblIntegral4(ratesReversed, VoltsFrameLength, GetPriceMA);
      var voltsAll = ratesForCorridor.Select(GetVoltage).Where(Lib.IsNotNaN).ToArray();
      var voltsAll2 = ratesForCorridor.Select(GetVoltage2).ToArray();
      OnGeneralPurpose(() => {
        var vh = voltsAll.AverageByIterations(VoltsHighIterations).DefaultIfEmpty().Average();
        GetVoltageHigh = () => vh;
        var va = voltsAll2.Average();
        GetVoltageAverage = () => va;
      });
      Func<Rate, double> distanceFunc = r => GetVoltage2(r).Abs();
      var distanceMin = UseRatesInternal(ri =>
        ri.TakeLast((VoltsAverageLength * 60).ToInt())
        .Select(distanceFunc)
        .Where(Lib.IsNotNaN)
        .Average() * CorridorDistance);
      var distanceSum = 0.0;
      var count = ratesReversed.Select(r => (distanceSum += distanceFunc(r))).TakeWhile(d => d < distanceMin).Count();
      Func<IList<Rate>, int> scan = rates => distanceMin == 0
        ? CorridorDistance
        : count;// rateChunks.SkipWhile(chunk => chunk.Sum(GetVoltage) < distanceMin).First().Count;
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan);
    }
  }
}
