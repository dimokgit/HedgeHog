using System;
using System.Reactive;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using HedgeHog;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
namespace HedgeHog.Tests {

  namespace UnitLib {
    [TestClass]
    public class EnumerableExTest {
      [TestMethod()]
      public void DistinctLastUntilChangedTest() {
        var source1 = new[] { new { i = 0, x = 2 }, new { i = 0, x = 1 }, new { i = 1, x = 3 }, new { i = 2, x = 4 }, new { i = 2, x = 5 } };
        var test1 = new[] { new { i = 0, x = 1 }, new { i = 1, x = 3 }, new { i = 2, x = 5 } };
        var test2 = new[] { new { i = 0, x = 1 }, new { i = 1, x = 3 }, new { i = 2, x = 4 } };
        var result1 = source1.DistinctLastUntilChanged(a => a.i).ToArray();
        Assert.IsTrue(test1.SequenceEqual(result1));
        Assert.IsFalse(test2.SequenceEqual(result1));
        var source2 = new[] { new { i = 0, x = 1 }, new { i = 1, x = 3 }, new { i = 2, x = 4 }, new { i = 2, x = 6 }, new { i = 2, x = 5 } };
        var result2 = source2.DistinctLastUntilChanged(a => a.i).ToArray();
        Assert.IsTrue(test1.SequenceEqual(result2));
      }
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
      [TestMethod]
      public void ListReverse() {
        var Nums = Enumerable.Range(0, 100000).ToArray();
        var swDict = new Dictionary<string, double>();
        Stopwatch sw = Stopwatch.StartNew();
        var t1 = Nums.TakeLast(1).Single();
        swDict.Add("1", sw.ElapsedMilliseconds); sw.Restart();
        var t2 = Nums.Reverse().Take(1).Single();
        swDict.Add("2", sw.ElapsedMilliseconds); sw.Restart();
        var t3 = Nums.BackwardsIterator().Take(1).Single();
        swDict.Add("3", sw.ElapsedMilliseconds); sw.Restart();
        Console.WriteLine("[{2}]{0}:{1:n1}ms" + Environment.NewLine + "{3}", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds, "Test", string.Join(Environment.NewLine, swDict.Select(kv => "\t" + kv.Key + ":" + kv.Value)));

      }
    }
  }
}
