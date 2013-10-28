using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace HedgeHog {
  public static partial class Lib {
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
      var levelsWithCrosses = levels.Aggregate(new { level = 0.0, indexes = new int[0], distAvg = 0.0 }.AsList(), (list, level) => {
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
      return levelsWithCrosses.Aggregate(new { level = 0.0, sumAvg = 0.0, indexes = new int[0] }.AsList(), (list, levelWithCrosses) => {
        var level = levelWithCrosses.level;
        var crosses = levelWithCrosses.indexes;
        if (crosses.Count() >= crossesCount * 2) {
          var crossesZipped = crosses.Zip(crosses.Skip(1), (t1, t2) => new { index1 = t1, index2 = t2 }).ToArray();
          if (crossesZipped.Any()) {
            var indexSums = crossesZipped.Aggregate(new { sum = 0.0, index = 0 }.AsList(), (l, t) => {
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
        .Aggregate(new { height = 0.0, distance = 0 }.AsList(), (list, height) => {
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
        .Aggregate(new { line = new double[0], height = 0.0, indexes = new int[0], distAvg = 0.0 }.AsList(), (list, height) => {
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
      return levelsWithCrosses.Aggregate(new { height = 0.0, sumAvg = 0.0, indexes = new int[0] }.AsList(), (list, levelWithCrosses) => {
        var line = levelWithCrosses.line;
        var height = levelWithCrosses.height;
        var crosses = levelWithCrosses.indexes;
        if (crosses.Count() >= crossesCount * 2) {
          var crossesZipped = crosses.Zip(crosses.Skip(1), (t1, t2) => new { index1 = t1, index2 = t2 }).ToArray();
          if (crossesZipped.Any()) {
            var indexSums = crossesZipped.Aggregate(new { sum = 0.0, index = 0 }.AsList(), (l, t) => {
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
         list.GroupBy(l => l.height.Sign()).Select(l => l.OrderBy(a=>a.sumAvg).First().height).ToArray());
    }

    public static IList<Tuple<double,int>> CrossedLevelsWithGap(this double[] values, double step) {
      if (values.Length < 3) return new[] { new Tuple<double, int>(double.NaN, 0) };
      Func<double, double, double> calcRatio = (d1, d2) => d1 < d2 ? d1 / d2 : d2 / d1;
      var min = values.Min();
      var max = values.Max();
      var steps = max.Sub(min).Div(step).ToInt();
      var heights = Enumerable.Range(1, steps - 1).Select(s => min + s * step).ToArray();
      return Partitioner.Create(heights,true).AsParallel()
        .Aggregate(new Tuple<double, int>(double.NaN, 0).AsList(), (list, height) => {
          var line = Enumerable.Repeat(height, values.Length).ToArray();
          var indexes = line.Crosses3(values).Select(t => t.Item2).ToArray();
          if (indexes.Length > 1) {
            var gap = indexes.Zip(indexes.Skip(1), (p, n) => n - p).Max();
            list.Add(new Tuple<double, int>(height, gap));
          }
          return list;
        });
    }

    public static double StDevByRegressoin(this IList<double> values, double[] coeffs = null) {
      if(coeffs == null || coeffs.Length == 0) coeffs = values.Regress(1);
      var line = new double[values.Count];
      coeffs.SetRegressionPrice(0, values.Count, (i, v) => line[i] = v);
      return line.Zip(values, (l, v) => l - v).ToArray().StDev();
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
    public static ParallelQuery<double> Mirror(this ParallelQuery<double> prices, double linePrice) {
      return prices.Select(p => linePrice * 2 - p);
    }
    public static IEnumerable<double> Mirror(this double[] prices, double linePrice) {
      return prices.Select(p => linePrice * 2 - p);
    }
  }
}
