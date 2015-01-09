using System;
using System.Reactive;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using HedgeHog;
using System.Collections.Generic;
namespace HedgeHog.Tests {

  namespace UnitLib {
    public static class Mixins {
    }
    [TestClass]
    public class EnumerableExTest {
      [TestMethod]
      public void BufferVerticalTest() {
        var rand = new Random();
        var input = Enumerable.Range(0, 1440).Select(i => rand.Next(-100, 100) * 0.01).ToArray();
        var values = input.BufferVertical2(d => d, 0.3, (b, t,c) => new { b, t,c }).ToArray();
        Assert.IsFalse(values.Min(a => a.c) == 0);
      }
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
        var Nums = Enumerable.Range(0, 10).Publish(nums => {
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
}
