using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static partial class Lib {
    public static IEnumerable<int> IteratonSequence(int start, int end) {
      return IteratonSequence(start, end, IteratonSequenceNextStep);
    }
    public static IEnumerable<int> IteratonSequence(int start, int end, Func<int, double, int> nextStep, double divider = 100.0) {
      for (var i = start; i <= end; i += nextStep(i, divider))
        yield return i;
    }
    public static IEnumerable<int> IteratonSequence(int start, int end, Func<int, int> nextStep) {
      for (var i = start; i.Between(start, end); i += nextStep(i))
        yield return i;
    }

    public static int IteratonSequenceNextStep(int rc, double divider = 100.0) {
      var s = (rc / divider);
      return s > 0 ? s.Ceiling() : s.Floor();
    }
    public static int IteratonSequenceNextStepPow(int rc, double power = 0.6, int loop = 0) {
      var e = 1 / power;
      var p = Math.Pow(power, loop == 0 ? 1 : e * loop);
      var s = Math.Pow(rc, p);
      return s.ToInt();
    }
    public static int IteratorLoop<T>(int start, int end, double divider, Func<bool, bool> isOk, Func<int, int, Func<bool, bool>, Func<int, int>, T> getCounter, Func<T, int> countMap) {
      Func<int, int> nextStep = i => Lib.IteratonSequenceNextStep(i, divider);
      var _isOk = isOk;
      while (true) {
        var c = countMap(getCounter(start, end, isOk, nextStep));
        if (nextStep(c).Abs() <= 1)
          return c;
        divider *= -2;
        start = c; end = start + nextStep(c) * 3;
        if (divider < 0) { _isOk = b => !isOk(b); } else { _isOk = isOk; }
      }

    }
    public static int IteratorLoopPow<T>(int start, int end, double divider, Func<bool, bool> isOk, Func<int, int, Func<bool, bool>, Func<int, int>, T> getCounter, Func<T, int> countMap) {
      Func<int, int, int> nextStep = (i, l) => Lib.IteratonSequenceNextStepPow(i, divider, l);
      var _isOk = isOk;
      for (var i = 0; true; i++) {
        Func<int, int> ns = j => nextStep(j, i);
        var c = countMap(getCounter(start, end, isOk, ns));
        if (ns(c).Abs() <= 1)
          return c;
        start = c; end = start + ns(c) * 3;
        if (i % 2 == 1) { _isOk = b => !isOk(b); } else { _isOk = isOk; }
      }

    }
  }
}
