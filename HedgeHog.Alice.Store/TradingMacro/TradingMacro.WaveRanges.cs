using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using HedgeHog;
using HedgeHog.Bars;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    double waveWidthAvgIterCnt = 2000;
    double _lastTotalMinuteStDev = double.MaxValue;
    int _lastTotalMinuteStDevStep = 1;
    private void ScanForWaveRanges(List<Rate> rates) {
      try {
        #region Average Wave Height Calc
        var makeWaves = MonoidsCore.ToFunc((List<Rate>)null, 0.0, 0, (rateses, period, cmaPasses) => {
          List<WaveRange> wr = GetWaveRanges(rateses, period, cmaPasses);

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
          if(wTail.Count < 3 || wTail.TotalSeconds < 3)
            wTail = new WaveRange();
          #endregion

          #region Wave Stats
          #region Stats Funcs
          Func<IList<WaveRange>, Func<WaveRange, double>, double> summ = (wrs0, v) => wrs0.Select(v).DefaultIfEmpty(double.NaN).Sum();
          Func<IList<WaveRange>, Func<WaveRange, double>, double> avg = (wrs0, v) =>
            summ(wrs0, w => v(w) * w.HSDRatio) / summ(wrs0, w => w.HSDRatio);
          Func<IList<WaveRange>, Func<WaveRange, double>, Func<WaveRange, double>, double> avg2 = (wrs0, v, d) =>
              summ(wrs0, w => v(w) * d(w)) / summ(wrs0, w => d(w));
          Func<Func<WaveRange, double>, double> avgUp = value => wr.Select(value).DefaultIfEmpty().ToArray().AverageByAverageUp();
          Func<IList<WaveRange>, Func<WaveRange, double>, double> avgStd = (wrs0, v) => {
            var sd = wrs0.StandardDeviation(v) * 2;
            var a = wrs0.Average(v);
            return wrs0.Select(w => v(w).Abs()).Where(d => d.Between(a - sd, a + sd)).Average();
          };
          Func<Func<WaveRange, double>, double> rsd = value => wr.Select(value).DefaultIfEmpty().Sum();
          #endregion

          var wrs = wr.SkipLast(wr.Count > 4 ? 1 : 0).Where(w => !w.Distance.IsNaNOrZero()).ToArray();

          var ws = new WaveRange(1) {
            Distance = wrs.ToArray(w => w.Distance).RelativeStandardDeviation().ToPercent(),
            DistanceCma = avg2(wrs, w => w.DistanceCma, w => w.Distance),
            DistanceByRegression = avg2(wrs, w => w.DistanceByRegression, w => w.Distance),
            WorkByHeight = rsd(w => w.WorkByHeight),
            WorkByTime = rsd(w => w.WorkByTime),
            Angle = wrs.StandardDeviation(w => w.Angle),
            TotalMinutes = wrs.ToArray(w => w.TotalMinutes).RelativeStandardDeviation().ToPercent(),
            HSDRatio = avg2(wrs, w => w.HSDRatio, w => 1 / w.Distance),
            Height = rsd(w => w.Height),
            StDev = avg2(wrs, w => w.StDev, w => w.Distance)
          };

          var wa = new WaveRange(1) {
            DistanceCma = avg2(wrs, w => w.DistanceCma, w => 1 / Math.Pow(w.Distance, 1 / 3.0)),
            DistanceByRegression = avg2(wrs, w => w.DistanceByRegression, w => 1 / Math.Pow(w.Distance, 1 / 3.0)),
            WorkByHeight = avg(wrs, w => w.WorkByHeight),
            WorkByTime = avg(wrs, w => w.WorkByTime),
            HSDRatio = avg2(wrs, w => w.HSDRatio, w => w.Distance),
            Height = avg(wrs, w => w.Height),
            StDev = avg2(wrs, w => w.StDev, w => 1 / w.Distance)
          };
          try {
            wa.Distance = avg2(wrs, w => w.Distance, w => 1 / w.TotalMinutes);
            wa.Angle = avgStd(wrs, w => w.Angle.Abs());
            wa.TotalMinutes = avg2(wrs, w => w.TotalMinutes, w => 1 / w.Distance);
          } catch(Exception exc) {
            Log = exc;
            return null;
          }
          #endregion
          #region Conditions
          wr.ForEach(w => w.IsFatnessOk = w.StDev <= wa.StDev);
          wr.ForEach(w => w.IsDistanceCmaOk = w.DistanceCma >= wa.DistanceCma);
          #endregion

          return new { wr, wTail, wa, ws };
        });

        #endregion

        if(!IsCorridorFrozen()) {
          var wrwt = makeWaves(rates, PriceCmaLevels, CmaPasses);
          if(wrwt == null)
            return;
          WaveRanges = wrwt.wr;
          WaveRangeTail = wrwt.wTail;
          WaveRangeSum = wrwt.ws;
          WaveRangeAvg = wrwt.wa;

          #region Adjust CmaPasses
          Action setCmaPasses = () => {
            try {
              Func<WaveRange, double> stDever = WaveSmoothFunc();
              var makeWaveses = MonoidsCore.ToFunc((IEnumerable<int>)null, ups => ups
              .Where(i => i > 0)
              .Select(cmaPasses => new { sd = stDever(makeWaves(rates, PriceCmaLevels, cmaPasses).ws), cmaPasses }));

              var up = makeWaveses(Enumerable.Range(1, 500)).ToArray();
              var bufferCount = rates.Count.Div(100).ToInt();
              var up2 = up
                .Buffer(rates.Count.Div(100).ToInt(), 1)
                .Where(b => b.Count == bufferCount)
                .Select(b => new { b, avg = b.Average(x => x.sd) })
                .OrderBy(x => x.avg)
                .ToArray();
              up2
                .Take(1)
                .SelectMany(x => x.b)
                .MinBy(x => x.sd)
                .OrderBy(x => x.cmaPasses)
                .Take(1)
                .ForEach(x => {
                  CmaPassesCalc = x.cmaPasses;
                  Log = new Exception(new { CmaPassesCalc } + "");
                });
            } catch(Exception exc) {
              Log = exc;
            }
          };
          if(CmaPassesMin > 0)
            _addHistoryOrdersBuffer.Push(setCmaPasses);
          #endregion
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

    private void ScanForWaveRanges2(List<Rate> rates) {
      try {
        #region Average Wave Height Calc
        var makeWaves = MonoidsCore.ToFunc((List<Rate>)null, 0.0, 0, (rateses, period, cmaPasses) => {
          List<WaveRange> wr = GetWaveRanges(rateses, period, cmaPasses);

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
          if(wTail.Count < 3 || wTail.TotalSeconds < 3)
            wTail = new WaveRange();
          #endregion

          #region Wave Stats
          #region Stats Funcs
          Func<IList<WaveRange>, Func<WaveRange, double>, double> summ = (wrs0, v) => wrs0.Select(v).DefaultIfEmpty(double.NaN).Sum();
          Func<IList<WaveRange>, Func<WaveRange, double>, double> avg = (wrs0, v) =>
            summ(wrs0, w => v(w) * w.HSDRatio) / summ(wrs0, w => w.HSDRatio);
          Func<IList<WaveRange>, Func<WaveRange, double>, Func<WaveRange, double>, double> avg2 = (wrs0, v, d) =>
              summ(wrs0, w => v(w) * d(w)) / summ(wrs0, w => d(w));
          Func<Func<WaveRange, double>, double> avgUp = value => wr.Select(value).DefaultIfEmpty().ToArray().AverageByAverageUp();
          Func<IList<WaveRange>, Func<WaveRange, double>, double> avgStd = (wrs0, v) => {
            var sd = wrs0.StandardDeviation(v) * 2;
            var a = wrs0.Average(v);
            return wrs0.Select(w => v(w).Abs()).Where(d => d.Between(a - sd, a + sd)).Average();
          };
          Func<Func<WaveRange, double>, double> rsd = value => wr.Select(value).DefaultIfEmpty().Sum();
          #endregion

          var wrs = wr.SkipLast(wr.Count > 4 ? 1 : 0).Where(w => !w.Distance.IsNaNOrZero()).ToArray();

          var ws = new WaveRange(1) {
            Distance = wrs.Select(w => w.Distance).PowerMeanPower(1 / TrendHeightPerc),
            DistanceCma = avg2(wrs, w => w.DistanceCma, w => w.Distance),
            DistanceByRegression = avg2(wrs, w => w.DistanceByRegression, w => w.Distance),
            WorkByHeight = rsd(w => w.WorkByHeight),
            WorkByTime = rsd(w => w.WorkByTime),
            Angle = wrs.StandardDeviation(w => w.Angle),
            TotalMinutes = wrs.ToArray(w => w.TotalMinutes).RelativeStandardDeviation().ToPercent(),
            HSDRatio = avg2(wrs, w => w.HSDRatio, w => 1 / w.Distance),
            Height = rsd(w => w.Height),
            StDev = wrs.Select(w => w.StDev).PowerMeanPower(.1),
            PipsPerMinute = RatesPipsPerMInute
          };

          var wa = new WaveRange(1) {
            DistanceCma = avg2(wrs, w => w.DistanceCma, w => 1 / Math.Pow(w.Distance, 1 / 3.0)),
            DistanceByRegression = avg2(wrs, w => w.DistanceByRegression, w => 1 / Math.Pow(w.Distance, 1 / 3.0)),
            WorkByHeight = avg(wrs, w => w.WorkByHeight),
            WorkByTime = avg(wrs, w => w.WorkByTime),
            HSDRatio = avg2(wrs, w => w.HSDRatio, w => w.Distance),
            Height = avg(wrs, w => w.Height),
            StDev = wrs.Select(w => w.StDev).PowerMeanPower(10)
          };
          try {
            wa.Distance = wrs.Select(w => w.Distance).PowerMeanPower(TrendHeightPerc);
            wa.Angle = wrs.Average(w => w.Angle.Abs());
            wa.TotalMinutes = wrs.Select(w => w.TotalMinutes).PowerMeanPower(TrendHeightPerc);
            wa.PipsPerMinute = wa.Distance / wa.TotalMinutes;
          } catch(Exception exc) {
            Log = exc;
            return null;
          }
          #endregion
          #region Conditions
          wr.ForEach(w => w.IsFatnessOk = w.StDev <= wa.StDev);
          wr.ForEach(w => w.IsDistanceCmaOk = w.DistanceCma >= wa.DistanceCma);
          #endregion

          return new { wr, wTail, wa, ws };
        });

        #endregion

        if(!IsCorridorFrozen()) {
          var wrwt = makeWaves(rates, PriceCmaLevels, CmaPasses);
          if(wrwt == null)
            return;
          WaveRanges = wrwt.wr;
          WaveRangeTail = wrwt.wTail;
          WaveRangeSum = wrwt.ws;
          WaveRangeAvg = wrwt.wa;

          #region Adjust CmaPasses
          Action setCmaPasses = () => {
            try {
              Func<WaveRange, double> stDever = WaveSmoothFunc();

              var makeWaveses = MonoidsCore.ToFunc((IEnumerable<int>)null, ups =>
              Partitioner.Create(ups.ToArray(), true)
                .AsParallel().Select(cmaPasses => new { sd = stDever(makeWaves(rates, PriceCmaLevels, cmaPasses).ws), cmaPasses }));

              var up = makeWaveses(Lib.IteratonSequence(1, 600, i => i.Div(200).Ceiling())).OrderBy(x => x.cmaPasses).ToList();
              var bufferCount = rates.Count.Div(100).ToInt();
              var cma = Enumerable.Range(0, up.Count - bufferCount)
                .Select(i => up.GetRange(i, bufferCount).AsIList())
                .Where(b => b.Count == bufferCount)
                .Select(b => new { b, avg = b.Average(y => y.sd) })
                .Aggregate((b1, b2) => b1.avg < b2.avg ? b1 : b2)
                .b.MinBy(x => x.sd)
                .OrderBy(x => x.cmaPasses)
                .First().cmaPasses;

              CmaPassesCalc = cma;
              CmaPasses = CmaPassesCalc;
            } catch(Exception exc) {
              Log = exc;
            }
          };
          if(CmaPassesMin > 0)
            //setCmaPasses();
            _addHistoryOrdersBuffer.Push(() => {
              var sw = Stopwatch.StartNew();
              setCmaPasses();
              Log = new Exception(new { CmaPassesCalc, CmaPasses, sw.ElapsedMilliseconds } + "");
            });
          #endregion
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

    class AdjustCmaPassesAsyncBuffer : AsyncBuffer<AdjustCmaPassesAsyncBuffer, Action> {
      public AdjustCmaPassesAsyncBuffer() : base() {

      }
      protected override Action PushImpl(Action context) {
        return context;
      }
    }
    AdjustCmaPassesAsyncBuffer _adjustCmaPassesAsyncBuffer = new AdjustCmaPassesAsyncBuffer();

    #region CmaPassesMin
    private int _CmaPassesMin = 5;
    [Category(categoryActive)]
    [WwwSetting(wwwSettingsCorridorCMA)]
    public int CmaPassesMin {
      get { return _CmaPassesMin; }
      set {
        if(_CmaPassesMin != value) {
          _CmaPassesMin = value;
          OnPropertyChanged("CmaPassesMin");
        }
      }
    }
    public int CmaPassesCalc { get; set; }
    #endregion

    private List<int> GetWaveRangesExtreams(List<Rate> rates, double period, int cmaPasses) {
      var maxCount = 1000;
      var bufferCount = (rates.Count).Div(maxCount).Max(1).ToInt();

      var ratesCma = GetCmas(rates, period, cmaPasses);
      var dmas = ratesCma
        .Select((t, i) => new { t, i })
        .DistinctUntilChanged(z => z.t.Item2.Sign(z.t.Item3))
        .Select(z => new { Price = z.t.Item2, StartDate = z.t.Item1.StartDate, z.i })
        .ToArray();

      var widths = dmas.Zip(dmas.Skip(1), (dma1, dma2) => (double)dma2.i - dma1.i).DefaultIfEmpty(0.0).ToArray();
      var widthAvg = widths.AverageByStDev().Average();
      var waveWidth = widthAvg.Div(bufferCount).ToInt();
      var ratesForWave2 = (BarPeriod == BarsPeriodType.t1
        ? dmas
        .Buffer(bufferCount)
        .Select(b => new { Price = b.Average(r => r.Price), StartDate = b.Last().StartDate })
        : ratesCma.Select(r => new { Price = r.Item2, r.Item1.StartDate })
        ).ToArray();

      var extreams = ratesForWave2.Extreams(waveWidth, r => r.Price, r => r.StartDate).ToArray();
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
    private List<int> GetWaveRangesExtreams2(List<Rate> rates, double period, int cmaPasses) {
      var maxCount = 1000;
      var bufferCount = (rates.Count).Div(maxCount).Max(1).ToInt();

      var ratesCma = GetCmas(rates, period, cmaPasses);
      var dmas = ratesCma
        .Select((t, i) => new { t, i })
        .DistinctUntilChanged(z => z.t.Item2.Sign(z.t.Item3))
        .Select(z => new { Price = z.t.Item2, StartDate = z.t.Item1.StartDate, z.i })
        .ToArray();

      var widthAvg = dmas.Pairwise((dma1, dma2) => (double)dma2.i - dma1.i).DefaultIfEmpty(0.0).AverageByStDev().Average();
      var waveWidth = widthAvg.Div(bufferCount).ToInt();

      var ratesForWave2 = (BarPeriod == BarsPeriodType.t1
        ? dmas
        .Buffer(bufferCount)
        .Select(b => new { Price = b.Average(r => r.Price), StartDate = b.Last().StartDate })
        : ratesCma.Select(r => new { Price = r.Item2, r.Item1.StartDate })
        ).ToArray();

      var extreams = ratesForWave2.Extreams(waveWidth, r => r.Price, r => r.StartDate).ToArray();
      Func<DateTime, Rate, Rate, bool> isBetween = (d, r1, r2) => d.Between(r1.StartDate, r2.StartDate);
      var extreams3 = extreams
        .DistinctUntilChanged(t => t.Item1)
        .SelectMany(w => rates.FuzzyIndex(w.Item2, isBetween))
        .ToList();
      var badExtramsCount = extreams3.Pairwise((p, n) => new { p, n }).Count(x => x.p >= x.n);
      if(badExtramsCount > 0) {
        //Log = new Exception(new { badExtramsCount } + "");
        extreams3.Sort();
      }
      return extreams3;
    }
    private List<WaveRange> GetWaveRanges(List<Rate> rates, double period, int cmaPasses) {
      rates = rates.ToList();
      rates.Reverse();
      IList<int> extreams = GetWaveRangesExtreams(rates, period, cmaPasses);
      return new[] { 0 }.Concat(extreams)
        .Zip(extreams, (p, n) => rates.GetRange(p, n - p))
        .Do(range => range.Reverse())
        .ToList(range => new WaveRange(range, PointSize, BarPeriod));
    }
  }
}
