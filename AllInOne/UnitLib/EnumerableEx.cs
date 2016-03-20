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
      public IEnumerable<U> ZipByDateTime<T1, T2, U>(IList<Tuple<DateTime, T1>> prime, IList<Tuple<DateTime, T2>> other, Func<Tuple<DateTime, T1>, Tuple<DateTime, T2>, U> map) {
        var j = 0;
        for(var i = 0; i < prime.Count;) {
          var datePrime = prime[i].Item1;
          if(j < other.Count && other[j].Item1 <= datePrime) {
            j++;
          } else {
            yield return map(prime[i], other[(j - 1).Max(0)]);
            i++;
          }
        }
      }
      [TestMethod()]
      public void ZipByDateTime() {
        var dateStart = DateTime.MinValue;
        var a = new[] { 0, 1, 2, 3, 4, 5 }.Select((n, i) => Tuple.Create(dateStart.AddDays(i), n)).ToArray();
        var b = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }.Select((n, i) => Tuple.Create(dateStart.AddDays(i / 2.0), n)).ToArray();
        var z = ZipByDateTime(a, b, (t1, t2) => new { t1, t2 }).ToArray();
        var z2 = a.Zip(b, (t1, t2) => new { t1, t2 }).ToArray();
        Assert.IsTrue(z.Select(x => x.t2.Item2).SequenceEqual(z2.Select(x => x.t2.Item2)));
      }
      [TestMethod()]
      public void ZipByDateTime1() {
        var dateStart = DateTime.MinValue.AddDays(1);
        var dateStart2 = DateTime.MinValue;
        var a = new[] { 0, 1, 2, 3, 4, 5 }.Select((n, i) => Tuple.Create(dateStart.AddDays(i), n)).ToArray();
        var b = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }.Select((n, i) => Tuple.Create(dateStart2.AddDays(i*1.5), n)).ToArray();
        var z = ZipByDateTime(a, b, (t1, t2) => new { t1, t2 }).ToArray();
        var z2 = a.Zip(b, (t1, t2) => new { t1, t2 }).ToArray();
        Assert.IsTrue(z.Select(x => x.t2.Item2).SequenceEqual(z2.Select(x => x.t2.Item2)));
      }
      [TestMethod()]
      public void ZipByDateTime2() {
        var dateStart = DateTime.MinValue.AddDays(1);
        var dateStart2 = DateTime.MinValue;
        var a = new[] { 0, 1, 2, 3, 4, 5 }.Select((n, i) => Tuple.Create(dateStart.AddDays(i), n)).ToArray();
        var b = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }.Select((n, i) => Tuple.Create(dateStart2.AddDays(i / 2.0), n)).ToArray();
        var z = ZipByDateTime(a, b, (t1, t2) => new { t1, t2 }).ToArray();
        var z2 = a.Zip(b, (t1, t2) => new { t1, t2 }).ToArray();
        Assert.IsTrue(z.Select(x => x.t2.Item2).SequenceEqual(z2.Select(x => x.t2.Item2)));
      }
      [TestMethod()]
      public void ZipByDateTime3() {
        var dateStart = DateTime.MinValue.AddDays(1);
        var dateStart2 = DateTime.MinValue;
        var a = new[] { 0, 1, 2, 3, 4, 5 }.Select((n, i) => Tuple.Create(dateStart.AddDays(i), n)).ToArray();
        var b = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }.Select((n, i) => Tuple.Create(dateStart2.AddDays(i / 3.0), n)).ToArray();
        var z = ZipByDateTime(a, b, (t1, t2) => new { t1, t2 }).ToArray();
        var z2 = a.Zip(b, (t1, t2) => new { t1, t2 }).ToArray();
        Assert.IsTrue(z.Select(x => x.t2.Item2).SequenceEqual(z2.Select(x => x.t2.Item2)));
      }
      [TestMethod()]
      public void ZipByDateTime31() {
        var dateStart = DateTime.MinValue.AddDays(0.9);
        var dateStart2 = DateTime.MinValue;
        var a = new[] { 0, 1, 2, 3, 4, 5 }.Select((n, i) => Tuple.Create(dateStart.AddDays(i), n)).ToArray();
        var b = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 }.Select((n, i) => Tuple.Create(dateStart2.AddDays(i / 3.0), n)).ToArray();
        var z = ZipByDateTime(a, b, (t1, t2) => new { t1, t2 }).ToArray();
        var z2 = a.Zip(b, (t1, t2) => new { t1, t2 }).ToArray();
        Assert.IsTrue(z.Select(x => x.t2.Item2).SequenceEqual(z2.Select(x => x.t2.Item2)));
      }
      [TestMethod()]
      public void ZipByDateTime4() {
        var dateStart = DateTime.MinValue;
        var dateStart2 = DateTime.MinValue.AddDays(1);
        var a = new[] { 0, 1, 2, 3, 4, 5 }.Select((n, i) => Tuple.Create(dateStart.AddDays(i), n)).ToArray();
        var b = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }.Select((n, i) => Tuple.Create(dateStart2.AddDays(i / 3.0), n)).ToArray();
        var z = ZipByDateTime(a, b, (t1, t2) => new { t1, t2 }).ToArray();
        var z2 = a.Zip(b, (t1, t2) => new { t1, t2 }).ToArray();
        Assert.IsTrue(z.Select(x => x.t2.Item2).SequenceEqual(z2.Select(x => x.t2.Item2)));
      }

      [TestMethod()]
      public void GetRangeTest() {
        var a = new[] { 0, 1, 2, 3, 4, 5 };
        var b = a.GetRange(2);
        Assert.IsTrue(a.Skip(4).SequenceEqual(b));
      }
      [TestMethod()]
      public void GroupByAdjacentOriginal() {
        var r = new Random();
        var data0 = Enumerable
          .Range(0, 100000)
          .Select(i => r.NextDouble())
          .ToList();
        var data = data0.Scan(
            new { date = DateTime.Now, value = 0.0 },
            (seed, d) => new { date = seed.date.AddSeconds(d), value = r.NextDouble() * 100 }
          ).ToList();
        var sec = 1.FromSeconds();
        var sw = new Stopwatch();
        sw.Start();
        var res2 = data.Select((d, i) => new { d, i })
          .DistinctUntilChanged(a => a.d.date.AddMilliseconds(-a.d.date.Millisecond));
        var scan = res2.Scan(new { start = 0, end = 0 },
          (seed, a) => seed.end == 0 ? new { start = 0, end = a.i } : new { start = seed.end + 1, end = a.i });
        var ranges = scan.Skip(1).Select(a => data.GetRange(a.start, a.end - a.start + 1));
        var res3 = ranges.Select(range => range.Average(a => a.value))
          .ToList();
        sw.Stop();
        Console.WriteLine(new { v = "New", sw.ElapsedMilliseconds, res3.Count });
        res2.Count();
        sw.Restart();
        var groupDist = data.GroupedDistinct(d => d.date.AddMilliseconds(-d.date.Millisecond), range => range.Average(a => a.value)).ToList();
        sw.Stop();
        groupDist.Count();
        Console.WriteLine(new { v = "Super", sw.ElapsedMilliseconds, groupDist.Count });
        Assert.IsTrue(sw.ElapsedMilliseconds < 100);
      }
      [TestMethod()]
      public void CrossesSmoothedTest() {
        var rads = Enumerable.Range(1, 90).Select(i => i * Math.PI / 180).ToArray();
        var sin = rads.Select(rad => Math.Sin(rad)).ToArray();
        var cos = rads.Select(rad => Math.Cos(rad)).ToArray();
        var zip3 = sin.CrossesSmoothed(cos);
        Assert.AreEqual(rads.Length, zip3.Count);
      }

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
        var values = input.BufferVertical2(d => d, 0.3, (b, t, c) => new { b, t, c }).ToArray();
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
          if(i == 0)
            return 11.Yield();
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
        swDict.Add("1", sw.ElapsedMilliseconds);
        sw.Restart();
        var t2 = Nums.Reverse().Take(1).Single();
        swDict.Add("2", sw.ElapsedMilliseconds);
        sw.Restart();
        var t3 = Nums.BackwardsIterator().Take(1).Single();
        swDict.Add("3", sw.ElapsedMilliseconds);
        sw.Restart();
        Console.WriteLine("[{2}]{0}:{1:n1}ms" + Environment.NewLine + "{3}", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds, "Test", string.Join(Environment.NewLine, swDict.Select(kv => "\t" + kv.Key + ":" + kv.Value)));

      }
      [TestMethod]
      public void TakeWhileWithCounter() {
        var ints = Enumerable.Range(0, 5).ToList();
        Console.WriteLine(new { ints });
        Assert.AreEqual(2, ints.BackwardsIterator().TakeWhile(i => i >= 4, 2).Last());
        Assert.AreEqual(0, ints.BackwardsIterator().TakeWhile(i => i >= 4, 20).Last());
      }
      [TestMethod]
      public void TakeFirst() {
        var ints = Enumerable.Range(1, 5).ToList();
        Assert.AreEqual(3, ints.TakeFirst(-2).Last());
        Assert.AreEqual(2, ints.TakeFirst(2).Last());
        Assert.AreEqual(5, ints.TakeFirst(20).Last());
        Assert.AreEqual(0, ints.TakeFirst(-20).DefaultIfEmpty().Last());
      }
    }
  }
}
