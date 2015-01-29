using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static partial class Lib {
    public static Func<int, int, Func<bool, bool>, Func<int, int>, T> GetIterator<T>(Func<int, int, Func<bool, bool>, Func<int, int>, T> process) {
      return process;
    }
    public static IEnumerable<int> IteratonSequence(int start, int end) {
      return IteratonSequence(start, end, IteratonSequenceNextStep);
    }
    public static IEnumerable<int> IteratonSequence(int start, int end, Func<int, double, int> nextStep, double divider = 100.0) {
      for (var i = start; i <= end; i += nextStep(i, divider))
        yield return i;
    }
    public static IEnumerable<int> IteratonSequence(int start, int end, Func<int, int> nextStep) {
      Func<int, int> getEnd = ns => start + (end - start).Div(ns).Ceiling() * ns;
      for (var i = start; i.Between(start, getEnd(nextStep(i))); i += nextStep(i))
        yield return i;
    }

    public static int IteratonSequenceNextStep(int rc) {
      return IteratonSequenceNextStep(rc, 100);
    }
    public static int IteratonSequenceNextStep(int rc, double divider) {
      var s = (rc / divider);
      return s > 0 ? s.Ceiling() : s.Floor();
    }
    public static Func<int, int, int> IteratonSequencePower(int maxCount, double ratio) {
      var x = ratio == 1 ? 1 : 1 / Math.Log(maxCount, ratio);
      return (count, loop) => IteratonSequenceNextStep(count, x, loop);
    }
    public static int IteratonSequenceNextStepPow(int rc, double power = 0.6, int loop = 0) {
      var e = 1 / power.Abs();
      var p = Math.Pow(power.Abs(), loop == 0 ? 1 : e * loop);
      var s = Math.Pow(rc, p);
      return s.ToInt() * power.Sign();
    }
    public static int IteratonSequenceNextStep(int count, double power = 1, int loop = 0) {
      var p = count / 100.0 * Math.Pow(count, power.Abs()) / Math.Pow(2, loop.Abs());
      return p.Ceiling() * power.Sign() * ((double)loop).SignUp();
    }
    public static int IteratorLoopPow<T>(int maxCount, double lastStepRatio, int start, int end, Func<int, int, Func<bool, bool>, Func<int, int>, T> getCounter, Func<T, int> countMap) {
      int s = start, e = end;
      var nsp = IteratonSequencePower(maxCount, lastStepRatio);
      var divider = 1;
      Func<int, int, int> nextStep = (i, l) => nsp(i, l) * divider;
      Func<bool, bool> skipWhile = b => b;
      bool doContinue = false;
      for (var i = 0; true; i++) {
        Func<int, int> ns = j => nextStep(j, i);
        var sw = i % 2 == 0 ? skipWhile : b => !skipWhile(b);
        var count = countMap(getCounter(s, e, sw, ns));
        var step = ns(s).Abs().Max(ns(e).Abs()) * divider;
        if (!count.Between(s - step, e + step))
          if (step.Abs() > 1) doContinue = true;
          else throw new Exception(new { func = "IteratorLoopPow: !count.between(start,end)", count, start = s, end = e } + "");
        if (ns(count).Abs() <= 1)
          return count;
        if (doContinue) continue;
        divider = -divider;
        e = s; s = count;// -ns(count) * 2; e = count + ns(count) * 3;
      }
    }
  }
}
