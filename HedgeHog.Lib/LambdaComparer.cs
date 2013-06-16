using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog {

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
