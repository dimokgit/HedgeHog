using System;
using System.Reactive;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using HedgeHog;

namespace UnitLib {
  [TestClass]
  public class EnumerableExTest {
    [TestMethod]
    public void TestFinaly() {
      Func<dynamic, dynamic> I = x => x;
      /* try { */
      /* try { */
      new[] {
        /* yield return */ 1,
        /* yield return */ 2 }.Concat(
        /* throw */        EnumerableEx.Throw<int>(new Exception()))
        .Finally(() =>
        Console.WriteLine("Finally"))
        .Catch((Exception ex) => new[] {
    /* yield return */ 3,
    /* yield return */ 4,
    /* yield return */ 5 })
        .Take(1)
        .Count();
    }
    [TestMethod]
    public void TestReverse() {
      var nums = Enumerable.Range(0, 1000000);
      Console.WriteLine("End: " + nums./*Do(Console.WriteLine).*/TakeLast(1).First());
    }
    [TestMethod]
    public void TestDistincUntilChanged() {
      var nums = new[] { 1, 1, 2, 2, 3, 3 };
      nums
        .DistinctUntilChanged(LambdaComparer.Factory<int>((i1, i2) => i1 == i2))
        .Do(Console.WriteLine)
        .Count();
    }
    [TestMethod]
    public void TestPublish() {
      var Nums = Enumerable.Range(0,10).Publish(nums => {
        var d = 3;
        return nums.Reverse().TakeWhile(num => num >= d);
      });
      Assert.IsTrue(Nums.Any());
    }
    [TestMethod]
    public void TestExpand() {
      var Nums = Enumerable.Range(0, 10).Expand(i => {
        if (i == 0) return 11.Yield();
        return 0.YieldBreak();
      });
      Assert.IsTrue(Nums.Do(Console.WriteLine).Count() == 11);
    }
  }
}
