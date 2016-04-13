using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using HedgeHog;
using HedgeHog.Bars;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    double waveWidthAvgIterCnt = 2000;
    private void ScanForWaveRanges(List<Rate> rates) {
      try {
        #region Average Wave Height Calc
        var makeWaves = MonoidsCore.ToFunc((List<Rate>)null, (rateses) => {
          List<WaveRange> wr = GetWaveRanges(rateses);

          #region Split First Wave
          var splitIndex = wr.Take(1)
          .Where(w => !w.IsEmpty)
          .Select(w => w.Range.IndexOf((w.Slope > 0
              ? w.Range.MaxBy(r => r.AskHigh)
              : w.Range.MinBy(r => r.AskLow)
              ).First()))
              .DefaultIfEmpty(-1)
              .First();
          WaveRange wTail = new WaveRange(0);
          wr.Take(1)
          .Where(w => !w.IsEmpty)
          .ToArray()
            .Select(w => w.Count)
            .Where(wrCount => splitIndex.Div(wrCount) > .1)
            .Select(wrCount => {
              var range = wr[0].Range;
              var wr0 = range.GetRange(0, splitIndex + 1).ToList();
              var rangeTail = range.GetRange(splitIndex + 1, range.Count - (splitIndex + 1));
              if(rangeTail.Any() && rangeTail.Last().StartDate.Subtract(rangeTail[0].StartDate).TotalSeconds > 3)
                return new { wr0, wrTail = new WaveRange(rangeTail, PointSize, BarPeriod) { IsTail = true } };
              return null;
            })
            .Where(x => x != null)
            .ForEach(x => {
              wr = new[] { x.wr0 }.Select(w => new WaveRange(w, PointSize, BarPeriod)).Concat(wr.Skip(1)).ToList();
              wTail = x.wrTail;
            });
          #endregion

          #region Wave Stats
          Func<IList<WaveRange>, Func<WaveRange, double>, double> summ = (wrs, v) => wrs.Select(v).DefaultIfEmpty(double.NaN).Sum();
          Func<IList<WaveRange>, Func<WaveRange, double>, double> avg = (wrs, v) =>
            summ(wrs, w => v(w) * w.HSDRatio) / summ(wrs, w => w.HSDRatio);
          Func<IList<WaveRange>, Func<WaveRange, double>, Func<WaveRange, double>, double> avg2 = (wrs, v, d) =>
              summ(wrs, w => v(w) * d(w)) / summ(wrs, w => d(w));
          Func<Func<WaveRange, double>, double> avgUp = value => wr.Select(value).DefaultIfEmpty().ToArray().AverageByAverageUp();
          var wa = new WaveRange(1) {
            Distance = avg2(wr, w => w.Distance, w => w.HSDRatio),
            DistanceCma = avg2(wr, w => w.DistanceCma, w => 1 / Math.Pow(w.Distance, 1 / 3.0)),
            DistanceByRegression = avg2(wr, w => w.DistanceByRegression, w => 1 / Math.Pow(w.Distance, 1 / 3.0)),
            WorkByHeight = avg(wr, w => w.WorkByHeight),
            WorkByTime = avg(wr, w => w.WorkByTime),
            Angle = avg2(wr, w => w.Angle.Abs(), w => w.HSDRatio),
            TotalMinutes = avg2(wr, w => w.TotalMinutes.Abs(), w => w.HSDRatio),
            HSDRatio = avg2(wr, w => w.HSDRatio, w => w.Angle.Abs()),
            Height = avg(wr, w => w.Height),
            StDev = wr.Average(w => w.StDev)
          };
          if(wTail.TotalSeconds < 3)
            wTail = new WaveRange();
          Func<Func<WaveRange, double>, double> rsd = value => wr.Select(value).DefaultIfEmpty().Sum();
          var ws = new WaveRange(1) {
            Distance = avg2(wr, w => w.Distance, w => 1 / w.HSDRatio),
            DistanceCma = avg2(wr, w => w.DistanceCma, w => w.Distance),
            DistanceByRegression = avg2(wr, w => w.DistanceByRegression, w => w.Distance),
            WorkByHeight = rsd(w => w.WorkByHeight),
            WorkByTime = rsd(w => w.WorkByTime),
            Angle = avg2(wr, w => w.Angle.Abs(), w => 1 / w.HSDRatio),
            TotalMinutes = avg2(wr, w => w.TotalMinutes.Abs(), w => 1 / w.HSDRatio),
            HSDRatio = avg2(wr, w => w.HSDRatio, w => 1 / w.Angle.Abs()),
            Height = rsd(w => w.Height),
            StDev = avg2(wr, w => w.StDev, w => w.HSDRatio)
          };
          #endregion
          #region Conditions
          wr.ForEach(w => w.IsFatnessOk = w.StDev <= wa.StDev);
          wr.ForEach(w => w.IsDistanceCmaOk = w.DistanceCma >= wa.DistanceCma);
          #endregion
          return new { wr, wTail, wa, ws };
        });

        #endregion

        if(!IsCorridorFrozen()) {
          var wrwt = makeWaves(rates);
          WaveRanges = wrwt.wr;
          WaveRangeTail = wrwt.wTail;
          WaveRangeSum = wrwt.ws;
          WaveRangeAvg = wrwt.wa;
        } else {
          var firstWaveRange = rates.BackwardsIterator().TakeWhile(r => r.StartDate >= WaveRanges[0].StartDate).Reverse().ToList();
          WaveRanges = new[] { new WaveRange(firstWaveRange, PointSize, BarPeriod) }.Concat(WaveRanges.Skip(1)).ToList();
          WaveRangeTail = new WaveRange(0);
        }

        Func<WaveRange, double> bfp = w => w.DistanceByRegression;// WaveRangeAvg.BestFitProp();
        WaveFirstSecondRatio = WaveRanges.Take(1).Select(w1 => bfp(w1) / bfp(WaveRangeAvg)).FirstOrDefault();

        WaveHeightAverage = WaveRangeAvg.Height;
        WaveHeightPower = new[]{
          WaveRangeAvg.WorkByHeight,
          WaveRangeAvg.Height ,
          WaveRangeAvg.DistanceByRegression
        }.StandardDeviation();
      } catch(Exception exc) {
        Log = exc;
      }
    }

    #region FatnessWeightPower
    private double _FatnessWeightPower = 5;
    [Category(categoryActive)]
    [WwwSetting]
    public double FatnessWeightPower {
      get { return _FatnessWeightPower; }
      set {
        if(_FatnessWeightPower != value) {
          _FatnessWeightPower = value;
          OnPropertyChanged("FatnessWeightPower");
        }
      }
    }

    #endregion

    private List<int> GetWaveRangesExtreams(List<Rate> rates) {
      var maxCount = 1000;
      var bufferCount = (rates.Count).Div(maxCount).Max(1).ToInt();

      var dmas = rates.Select((r, i) => new { r, i })
        .Zip(rates, (r, ma) => new { r, ma = ma.PriceRsiP })
        .DistinctUntilChanged(x => x.r.r.PriceCMALast.Sign(x.ma))
        .ToArray();

      var widths = dmas.Zip(dmas.Skip(1), (dma1, dma2) => (double)dma2.r.i - dma1.r.i).DefaultIfEmpty(0.0).ToArray();
      var widthAvg = widths.AverageByStDev().Average();
      var waveWidth = widthAvg.Div(bufferCount).ToInt();

      var ratesForWave = BarPeriod == BarsPeriodType.t1
        ? rates
          .Buffer(bufferCount)
          .Select(b => new { Price = b.Average(r => r.PriceCMALast), StartDate = b.Last().StartDate })
        : rates.Select(r => new { Price = r.PriceCMALast, StartDate = r.StartDate });
      var extreams = ratesForWave.Extreams(waveWidth, r => r.Price, r => r.StartDate).ToArray();
      var slopeMin = extreams.Select(t => t.Item3.Abs()).Average();
      var extreams2 = extreams.Scan(Tuple.Create(0, DateTime.Now, 0.0), (p, t) => {
        return p.Item1 == 0 ? t
          : t.Item1 - p.Item1 < waveWidth.Div(waveWidthAvgIterCnt)
          //: t.Item3.Abs() > slopeMin.Div(waveWidthAvgIterCnt)
          ? p
          : p.Item3.SignUp() == t.Item3.SignUp()
          ? p
          : t;
      });
      Func<DateTime, Rate, Rate, bool> isBetween = (d, r1, r2) => d.Between(r1.StartDate, r2.StartDate);
      Func<List<Tuple<int, DateTime, double>>> newList = () => new List<Tuple<int, DateTime, double>>();
      var extreams21 = extreams.Scan(newList(), (l, t) => {
        //if(l.Count > 0 && t.Item1 - l[0].Item1 > waveWidth / 2)
        if(l.Count > 0 && t.Item3.Abs() > slopeMin / waveWidthAvgIterCnt)
          l = newList();
        l.Add(t);
        return l;
      })
      .Select(l => l[0])
      .DistinctLastUntilChanged(t => t.Item3.SignUp())
      //.Select(g => g.Last())
      .ToArray();
      var extreams3 = extreams21
        .DistinctUntilChanged(t => t.Item1)
        .SelectMany(w => rates.FuzzyIndex(w.Item2, isBetween))
        .ToList();
      var badExtramsCount = extreams3.Pairwise((p, n) => new { p, n }).Count(x => x.p > x.n);
      if(badExtramsCount > 0) {
        //Log = new Exception(new { badExtramsCount } + "");
        extreams3.Sort();
      }
      return extreams3;
    }
    private List<WaveRange> GetWaveRanges(List<Rate> rates) {
      rates = rates.ToList();
      rates.Reverse();
      IList<int> extreams = GetWaveRangesExtreams(rates);
      return new[] { 0 }.Concat(extreams)
        .Zip(extreams, (p, n) => rates.GetRange(p, n - p))
        .Do(range => range.Reverse())
        .ToList(range => new WaveRange(range, PointSize, BarPeriod));
    }
  }
}
