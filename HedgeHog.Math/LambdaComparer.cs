using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog {
  public static class GroupMixin {
    public static Comparison<T> AsComparison<T>(this Func<T, T, int> lambda) {
      return new Comparison<T>(lambda);
    }
    public static List<T> SortByLambda<T>(this List<T> list, Func<T, double> lambda) {
      return list.SortByLambda((a, b) => Math.Sign(lambda(a) - lambda(b)));
    }
    public static List<T> SortByLambda<T>(this List<T> list, Func<T, T, bool> lambda) {
      list.SortByLambda((a, b) => a.Equals(b) ? 0 : lambda(a, b) ? -1 : 1);
      return list;
    }
    public static List<T> SortByLambda<T>(this List<T> list, Func<T, T, int> lambda) {
      list.Sort(lambda.AsComparison());
      return list;
    }
    public static IEnumerable<IGrouping<T, T>> GroupByCloseness<T>(this IEnumerable<T> list, double delta, Func<T, T, double, bool> groupBy) {
      return list.GroupBy(t => t, new ClosenessComparer<T>(delta, groupBy));
    }
    public static IEnumerable<T> Distinct<T>(this ParallelQuery<T> list, Func<T, T, bool> distinctBy) {
      return list.Distinct(new LambdaComparer<T>(distinctBy));
    }
    public static IEnumerable<IGrouping<T, T>> GroupByLambda<T>(this IList<T> list, Func<T, T, bool> groupBy) {
      return list.GroupBy(t => t, new LambdaComparer<T>(groupBy));
    }
    public static IDictionary<T,IList<T>> RunningGroup<T>(this IEnumerable<T> values, Func<T, T, bool> same) {
      return values.Aggregate(new[] { new { key = default(T), values = new List<T>() } }.ToList(),
        (g, v) => {
          if (g.Count == 0 || !same(v, g.Last().values.Last()))
            g.Add(new { key = v, values = new List<T>() { v } });
          else
            g.Last().values.Add(v);
          return g;
        }).ToDictionary(a => a.key, a => (IList<T>)a.values);
    }
  }
  public class ClosenessComparer<T> : IEqualityComparer<T> {
    private readonly double delta;
    private readonly Func<T, T, double, bool> compare;

    public ClosenessComparer(double delta, Func<T, T, double, bool> compare) {
      this.delta = delta;
      this.compare = compare;
    }

    public bool Equals(T x, T y) {
      return compare(x, y, delta);
    }

    public int GetHashCode(T obj) {
      return 0;
    }
  }
  public static class LambdaComparer {
    public static LambdaComparer<T> Factory<T>(Func<T, T, bool> lambda) {
      return new LambdaComparer<T>(lambda);
    }

  }
  public static class LambdaComparisson {
    public static Comparison<T> Factory<T>(Func<T, T, int> lambda) {
      return new Comparison<T>(lambda);
    }
    public static Comparison<T> Factory<T>(Func<T, T, bool> lambda) {
      return new Comparison<T>(new Func<T, T, int>((a, b) => a.Equals(b) ? 0 : lambda(a, b) ? 1 : -1));
    }
  }
  public class LambdaComparer<T> : IEqualityComparer<T> {
    private readonly Func<T, T, bool> _lambdaComparer;
    private readonly Func<T, int> _lambdaHash;

    public LambdaComparer(Func<T, T, bool> lambdaComparer) :
      this(lambdaComparer, o => 0) {
    }

    public LambdaComparer(Func<T, T, bool> lambdaComparer, Func<T, int> lambdaHash) {
      if (lambdaComparer == null)
        throw new ArgumentNullException("lambdaComparer");
      if (lambdaHash == null)
        throw new ArgumentNullException("lambdaHash");

      _lambdaComparer = lambdaComparer;
      _lambdaHash = lambdaHash;
    }

    public bool Equals(T x, T y) {
      return _lambdaComparer(x, y);
    }

    public int GetHashCode(T obj) {
      return _lambdaHash(obj);
    }
  }
}
