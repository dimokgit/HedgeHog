using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Reflection;
using ReactiveUI;

namespace HedgeHog {
  public static partial class Lib {
    public static List<T> InnerList<T>(this ReactiveUI.ReactiveList<T>source){
      var field = typeof(ReactiveList<T>).GetField("_inner", BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.Instance);
      return (List<T>)field.GetValue(source);
    }
    public static List<T> CopyLast<T>(this ReactiveList<T> source, int count) {
      return source.ToList().CopyLast(count);
    }
    public static List<T> CopyLast<T>(this List<T> source, int count) {
      var startIndex = Math.Max(0, source.Count - count);
      return source.GetRange(startIndex, source.Count - startIndex);
    }
    public static T[] CopyLast<T>(this T[] source, int count) {
      var startIndex = Math.Max(0, source.Length - count);
      return source.CopyToArray(startIndex, source.Length - startIndex);
    }
    public static IEnumerable<U> Reverse<T, U>(this IEnumerable<T> source, Func<T, U> projector) {
      return source.Reverse().Select(projector);
    }
    public static IEnumerable<U> Reverse<T, U>(this IEnumerable<T> source, Func<T, int, U> projector) {
      return source.Reverse().Select(projector);
    }
    public class EdgeInfo {
      public double Level { get; set; }
      public double SumAvg { get; set; }
      public int[] Indexes { get; set; }
      public double DistAvg { get; set; }
      public EdgeInfo(double level, double sumAvg, int[] indexes) {
        this.Level = level;
        this.SumAvg = sumAvg;
        this.Indexes = indexes;
        this.DistAvg = indexes.Zip(indexes.Skip(1), (i1, i2) => (double)i2 - i1).ToArray().AverageByIterations(1).Average() * indexes.Length;
      }
      public override string ToString() {
        return new { Level, SumAvg, DistAvg, Indexes = string.Join(",", Indexes) }.ToString();
      }
    }
    public static IList<EdgeInfo> Edge(this double[] values, double step, int crossesCount) {
      if (values.Length < crossesCount * 3) return new EdgeInfo[0];
      Func<double, double, double> calcRatio = (d1, d2) => d1 < d2 ? d1 / d2 : d2 / d1;
      var min = values.Min();
      var max = values.Max();
      var height = max - min;
      var linesCount = (height / step).ToInt();

      var levels = ParallelEnumerable.Range(0, linesCount).Select(level => min + level * step).ToArray();
      var levelsWithCrosses = levels.Aggregate(new { level = 0.0, indexes = new int[0], distAvg = 0.0 }.YieldBreakAsList(), (list, level) => {
        var line = Enumerable.Repeat(level, values.Count()).ToArray();
        var indexes = values.Crosses3(line).Select(t => t.Item2).ToArray();
        if (indexes.Length > 1) {
          var distanses = indexes.Zip(indexes.Skip(1), (p, n) => (double)n - p).ToArray();
          var distAvg = distanses.AverageByIterations(3).Average();
          list.Add(new { level, indexes, distAvg });
        }
        return list;
      }, list => list.ToArray());
      //levelsWithCrosses = levelsWithCrosses.OrderByDescending(l => l.distAvg).ToArray();
      //levelsWithCrosses = levelsWithCrosses.AverageByIterations(l => l.distAvg, (v, a) => v >= a, 3).ToArray();
      return levelsWithCrosses.Aggregate(new { level = 0.0, sumAvg = 0.0, indexes = new int[0] }.YieldBreakAsList(), (list, levelWithCrosses) => {
        var level = levelWithCrosses.level;
        var crosses = levelWithCrosses.indexes;
        if (crosses.Count() >= crossesCount * 2) {
          var crossesZipped = crosses.Zip(crosses.Skip(1), (t1, t2) => new { index1 = t1, index2 = t2 }).ToArray();
          if (crossesZipped.Any()) {
            var indexSums = crossesZipped.Aggregate(new { sum = 0.0, index = 0 }.YieldBreakAsList(), (l, t) => {
              var frame = t.index2 - t.index1;
              var frameValues = new double[frame];
              Array.Copy(values, t.index1, frameValues, 0, frameValues.Length);
              var upDowns = frameValues.Select(v => v - level).GroupBy(v => Math.Sign(v)).ToArray();
              var sum = upDowns.Select(g => new { key = g.Key, sum = Math.Abs(g.Sum()) }).OrderBy(g => g.sum).Last().sum;
              l.Add(new { sum, index = t.index1 });
              return l;
            });
            {
              indexSums = indexSums.AverageByIterations(a => a.sum, (v, avg) => v <= avg, 2).ToList();
              var groupped = indexSums.GroupByCloseness(values.Length / crossesCount / 2.0, (a1, a2, d) => a2.index - a1.index < d);
              indexSums = groupped.Select(g => { var f = g.OrderBy(v => v.sum).First(); return new { f.sum, f.index }; }).ToList();
            }
            indexSums = indexSums.OrderBy(s => s.sum).Take(crossesCount).ToList();
            if (indexSums.Count >= crossesCount) {
              var sumAvg = indexSums.Average(s => s.sum);
              var indexes = indexSums.Select(s => s.index).OrderBy(i => i).ToArray();
              list.Add(new { level, sumAvg, indexes });
            }
          }
        }
        return list;
      }, list => //!list.Any() ? crossesCount > 1 ? values.Edge(step, crossesCount - 1) : new EdgeInfo[0]:
         list.OrderBy(l => l.sumAvg).Select(l => new EdgeInfo(l.level, l.sumAvg, l.indexes)).ToArray());
    }

    public static IList<double> Edge(this double[] values, double step, double minimumHeight) {
      if (values.Length < 2) return new[] { double.NaN, double.NaN };
      var middleLine = values.Line();
      var middleLineZipped = middleLine.Zip(values, (v1, v2) => v2 - v1).ToArray();
      var min = middleLineZipped.Where(v => v < 0).Min();
      var max = middleLineZipped.Where(v => v > 0).Max();
      var heights = new List<double>();
      while (min < max)
        heights.Add(min += step);
      return heights.Where(h => !h.Between(-minimumHeight, minimumHeight))
        .Aggregate(new { height = 0.0, distance = 0 }.YieldBreakAsList(), (list, height) => {
          var line = middleLine.Select(ml => ml + height).ToArray();
          var indexes = line.Crosses3(values).Select(t => t.Item2).ToArray();
          var distanses = indexes.Zip(indexes.Skip(1), (p, n) => n - p).ToArray();
          if (distanses.Any())
            list.Add(new { height, distance = distanses.Max() });
          return list;
        }, list => list.GroupBy(a => a.height.Sign()).Select(a => a.OrderByDescending(a1 => a1.distance).First().height).ToArray());
    }

    public static IList<double> EdgeByRegression(this double[] values, double step, int crossesCount, double heightMin) {
      if (values.Length < crossesCount * 3) return new[] { double.NaN, double.NaN };
      Func<double, double, double> calcRatio = (d1, d2) => d1 < d2 ? d1 / d2 : d2 / d1;
      var middleLine = values.Line();
      var middleLineZipped = middleLine.Zip(values, (v1, v2) => v2 - v1).ToArray();
      var min = middleLineZipped.Where(v => v < 0).Min();
      var max = middleLineZipped.Where(v => v > 0).Max();
      var heights = new List<double>();
      while (min < max)
        heights.Add(min += step);
      var levelsWithCrosses = heights.Where(h => !h.Between(-heightMin, heightMin))
        .Aggregate(new { line = new double[0], height = 0.0, indexes = new int[0], distAvg = 0.0 }.YieldBreakAsList(), (list, height) => {
          var line = middleLine.Select(ml => ml + height).ToArray();
          var indexes = line.Crosses3(values).Select(t => t.Item2).ToArray();
          if (indexes.Length >= 4) {
            var distanses = indexes.Zip(indexes.Skip(1), (p, n) => (double)n - p).ToArray();
            var distAvg = distanses.AverageByIterations(3).Average();
            list.Add(new { line, height, indexes, distAvg });
          }
          return list;
        }, list => list.ToArray());
      //levelsWithCrosses = levelsWithCrosses.OrderByDescending(l => l.distAvg).ToArray();
      //levelsWithCrosses = levelsWithCrosses.AverageByIterations(l => l.distAvg, (v, a) => v >= a, 3).ToArray();
      return levelsWithCrosses.Aggregate(new { height = 0.0, sumAvg = 0.0, indexes = new int[0] }.YieldBreakAsList(), (list, levelWithCrosses) => {
        var line = levelWithCrosses.line;
        var height = levelWithCrosses.height;
        var crosses = levelWithCrosses.indexes;
        if (crosses.Count() >= crossesCount * 2) {
          var crossesZipped = crosses.Zip(crosses.Skip(1), (t1, t2) => new { index1 = t1, index2 = t2 }).ToArray();
          if (crossesZipped.Any()) {
            var indexSums = crossesZipped.Aggregate(new { sum = 0.0, index = 0 }.YieldBreakAsList(), (l, t) => {
              var frame = t.index2 - t.index1;
              var frameValues = new double[frame];
              Array.Copy(values, t.index1, frameValues, 0, frameValues.Length);
              var lineValues = new double[frame];
              Array.Copy(line, t.index1, lineValues, 0, frame);
              var upDowns = frameValues.Zip(lineValues, (v, level) => v - level).GroupBy(v => Math.Sign(v)).ToArray();
              var sum = upDowns.Select(g => new { key = g.Key, sum = Math.Abs(g.Sum()) }).OrderBy(g => g.sum).Last().sum;
              l.Add(new { sum, index = t.index1 });
              return l;
            });
            {
              indexSums = indexSums.AverageByIterations(a => a.sum, (v, avg) => v <= avg, 2).ToList();
              var groupped = indexSums.GroupByCloseness(values.Length / crossesCount / 4.0, (a1, a2, d) => a2.index - a1.index < d);
              indexSums = groupped.Select(g => { var f = g.OrderBy(v => v.sum).First(); return new { f.sum, f.index }; }).ToList();
            }
            indexSums = indexSums.OrderBy(s => s.sum).Take(crossesCount).ToList();
            if (indexSums.Count >= crossesCount) {
              var sumAvg = indexSums.Average(s => s.sum);
              var indexes = indexSums.Select(s => s.index).OrderBy(i => i).ToArray();
              list.Add(new { height, sumAvg, indexes });
            }
          }
        }
        return list;
      }, list => //!list.Any() ? crossesCount > 1 ? values.Edge(step, crossesCount - 1) : new EdgeInfo[0]:
         list.GroupBy(l => l.height.Sign()).Select(l => l.OrderBy(a => a.sumAvg).First().height).ToArray());
    }


    public static IEnumerable<Tuple<double, double>> EdgeByStDev(this double[] values, double step, int smootheCount) {
      if(values.Length < 3)
        return new[] { new Tuple<double, double>(double.NaN, 0.0) };
      var min = values.Min();
      var max = values.Max();
      var steps = max.Sub(min).Div(step).ToInt();
      var levels = Enumerable.Range(1, steps - 1).Select(s => min + s * step);
      var tuples = levels
        .AsParallel()
        .Select(level => {
          var line = Enumerable.Repeat(level, values.Length).ToArray();
          var heights = values.Zip(line, (lvl, ln) => lvl.Abs(ln));
          var stDev = heights.RelativeStandardDeviationSmoothed(smootheCount);
          return Tuple.Create(level, stDev);
        });
      return tuples.OrderBy(t => t.Item2);
    }

    public static IEnumerable<Tuple<double, double>> EdgeByAverage(this IList<double> values, double step) {
      if(values.Count < 3)
        return new[] { new Tuple<double, double>(double.NaN, 0.0) };
      var min = values.Min();
      var max = values.Max();
      var steps = max.Sub(min).Div(step).ToInt();
      var levels = Enumerable.Range(1, steps - 1).Select(s => min + s * step).AsParallel();
      var tuples = levels
        .Select(level => Tuple.Create(level, values.Select(v => v.Abs(level)).Average()));
      return tuples.OrderBy(t => t.Item2);
    }



    public static IList<Tuple<double, int>> CrossedLevelsWithGap(this double[] values, double step) {
      if(values.Length < 3)
        return new[] { new Tuple<double, int>(double.NaN, 0) };
      Func<double, double, double> calcRatio = (d1, d2) => d1 < d2 ? d1 / d2 : d2 / d1;
      var min = values.Min();
      var max = values.Max();
      var steps = max.Sub(min).Div(step).ToInt();
      var heights = Enumerable.Range(1, steps - 1).Select(s => min + s * step).ToArray();
      return Partitioner.Create(heights, true).AsParallel()
        .Aggregate(new Tuple<double, int>(double.NaN, 0).YieldBreakAsList(), (list, height) => {
          var line = Enumerable.Repeat(height, values.Length).ToArray();
          var indexes = line.Crosses3(values).Select(t => t.Item2).ToArray();
          if(indexes.Length > 1) {
            var gap = indexes.Zip(indexes.Skip(1), (p, n) => n - p).Max();
            list.Add(new Tuple<double, int>(height, gap));
          }
          return list;
        });
    }

    public static T MergeLeft<T, K, V>(this T me, params IDictionary<K, V>[] others)
        where T : IDictionary<K, V>, new() {
      T newMap = new T();
      foreach (IDictionary<K, V> src in
          (new List<IDictionary<K, V>> { me }).Concat(others)) {
        // ^-- echk. Not quite there type-system.
        foreach (KeyValuePair<K, V> p in src) {
          newMap[p.Key] = p.Value;
        }
      }
      return newMap;
    }
    public static IEnumerable<IGrouping<int, T>> Gaps<T>(this IEnumerable<T> values, double level, Func<T, double> valueFunc) {
      return values.ChunkBy(value => valueFunc(value).SignUp(level)).Skip(1).SkipLast(1);
    }
    public static IEnumerable<IEnumerable<IGrouping<int, T>>> PriceRangeGaps<T>(this IList<T> rates
      , double point
      , Func<T, double> getPrice
      , Func<double?, double> toSteps) {
      return from revs in rates.Yield()
             where rates.Count > 0
             let min = getPrice(rates.MinBy(getPrice).First())
             let max = getPrice(rates.MaxBy(getPrice).First())
             let range = toSteps(max - min).ToInt()
             from level in Enumerable.Range(0, range).Select(i => min + i * point).AsParallel()
             from gap in revs.Gaps(level, getPrice).Skip(1).SkipLast(1)
             group gap by level into gapsGrouped
             orderby gapsGrouped.Where(g => g.Key == 1).Select(g => g.Count()).OrderByDescending(c => c).FirstOrDefault()
                   + gapsGrouped.Where(g => g.Key == -1).Select(g => g.Count()).OrderByDescending(c => c).FirstOrDefault()
                   descending
             select gapsGrouped.OrderByDescending(g => g.Count())
      ;
    }

    public static void Zip<T1, T2, U>(this IEnumerable<(DateTime d, T1 t)> prime, IEnumerable<(DateTime d, T2 t)> other, Action<(DateTime, T1 t), (DateTime d, T2 t)> map) {
      prime.Zip(other, (a, b) => {
        map(a, b);
        return true;
      }).Count();
    }
    public static IEnumerable<U> Zip<T1, T2, U>(this IEnumerable<(DateTime d, T1 t)> prime, IEnumerable<(DateTime d, T2 t)> other, Func<(DateTime, T1 t), (DateTime d, T2 t), U> map) {
      (DateTime d, T2 t) prev = default;
      var isPrevSet = false;
      bool otherIsDone = false;
      using(var iterPrime = prime.GetEnumerator()) {
        using(var iterOther = other.GetEnumerator())
          if(iterOther.MoveNext()) {
            while(iterPrime.MoveNext()) {
              while(!otherIsDone && iterOther.Current.d <= iterPrime.Current.d) {
                prev = iterOther.Current;
                isPrevSet = true;
                if(otherIsDone = !iterOther.MoveNext())
                  break;
                continue;
              }
              yield return map(iterPrime.Current, isPrevSet ? prev: iterOther.Current);
            }
          }
      }
    }
    public static IEnumerable<U> Zip<T1, T2, U>(this IEnumerable<Tuple<DateTime, T1>> prime, IEnumerable<Tuple<DateTime, T2>> other, Func<Tuple<DateTime, T1>, Tuple<DateTime, T2>, U> map) {
      Tuple<DateTime, T2> prev = null;
      bool otherIsDone = false;
      using(var iterPrime = prime.GetEnumerator()) {
        using(var iterOther = other.GetEnumerator())
          if(iterOther.MoveNext()) {
            while(iterPrime.MoveNext()) {
              while(!otherIsDone && iterOther.Current.Item1 <= iterPrime.Current.Item1) {
                prev = iterOther.Current;
                if(otherIsDone = !iterOther.MoveNext())
                  break;
                continue;
              }
              yield return map(iterPrime.Current, prev ?? iterOther.Current);
            }
          }
      }
    }
    public static void Zip<T1, T2>(this IEnumerable<Tuple<DateTime, T1>> prime, IEnumerable<Tuple<DateTime, T2>> other, Action<Tuple<DateTime, T1>, Tuple<DateTime, T2>> map) {
      Tuple<DateTime, T2> prev = null;
      bool otherIsDone = false;
      using(var iterPrime = prime.GetEnumerator()) {
        using(var iterOther = other.GetEnumerator())
          if(iterOther.MoveNext()) {
            while(iterPrime.MoveNext()) {
              while(!otherIsDone && iterOther.Current.Item1 <= iterPrime.Current.Item1) {
                prev = iterOther.Current;
                if(otherIsDone = !iterOther.MoveNext())
                  break;
                continue;
              }
              map(iterPrime.Current, prev ?? iterOther.Current);
            }
          }
      }
    }
    public static void Zip<T1, T2>(this IEnumerable<T1> prime, Func<T1,DateTime> getDate, IEnumerable<Tuple<DateTime, T2>> other, Action<T1, Tuple<DateTime, T2>> map) {
      Tuple<DateTime, T2> prev = null;
      bool otherIsDone = false;
      using(var iterPrime = prime.GetEnumerator()) {
        using(var iterOther = other.GetEnumerator())
          if(iterOther.MoveNext()) {
            while(iterPrime.MoveNext()) {
              while(!otherIsDone && iterOther.Current.Item1 <= getDate(iterPrime.Current)) {
                prev = iterOther.Current;
                if(otherIsDone = !iterOther.MoveNext())
                  break;
                continue;
              }
              map(iterPrime.Current, prev ?? iterOther.Current);
            }
          }
      }
    }
  }
}
