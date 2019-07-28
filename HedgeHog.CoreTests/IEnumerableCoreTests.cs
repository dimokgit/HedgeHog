using Microsoft.VisualStudio.TestTools.UnitTesting;
using HedgeHog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Core;

namespace HedgeHog.Tests {
  [TestClass()]
  public class IEnumerableCoreTests {
    [TestMethod()]
    public void GetRangeByDates() {
      var start = DateTime.Parse("1/2/2000");
      var start2 = DateTime.Parse("1/2/1999");
      var end = DateTime.Parse("1/3/2000");
      var end2 = DateTime.Parse("1/30/2000");
      var data = new[] {
        new { d = DateTime.Parse("1/1/2000") },
        new { d = start},
        new { d = end },
        new { d = DateTime.Parse("1/4/2000") }
      }.ToList();
      var range = data.GetRange(start, end, a => a.d);
      Assert.AreEqual(start, range[0].d);
      Assert.AreEqual(end, range[1].d);
      Console.WriteLine(range.ToJson());

      range = data.GetRange(start2, end, a => a.d);
      Console.WriteLine(range.ToJson());
      Assert.AreEqual(data[0].d, range[0].d);
      Assert.AreEqual(end, range.Last().d);

      range = data.GetRange(start, end2, a => a.d);
      Console.WriteLine(range.ToJson());
      Assert.AreEqual(start, range[0].d);
      Assert.AreEqual(data.Last().d, range.Last().d);

      range = data.GetRange(start2, end2, a => a.d);
      Console.WriteLine(range.ToJson());
      Assert.AreEqual(data[0].d, range[0].d);
      Assert.AreEqual(data.Last().d, range.Last().d);
    }

    [TestMethod()]
    public void SingleOrElse() {
      Assert.AreEqual(3, new[] { 1, 2 }.SingleOrElse(() => 3));
      Assert.AreEqual(3, new[] { 3 }.SingleOrElse(() => 3));
    }

    [TestMethod()]
    [TestCategory("Busness Days")]
    public void AddBusinessDays() {
      var start = DateTime.Now.Date.GetNextWeekday(DayOfWeek.Saturday);
      Console.WriteLine(new { start = new { start, start.DayOfWeek } });
      Assert.AreEqual(start.GetNextWeekday(DayOfWeek.Monday), start.AddBusinessDays(0));
      Assert.AreEqual(0, start.AddBusinessDays(0).GetWorkingDays(start.GetNextWeekday(DayOfWeek.Monday)));
      Assert.AreEqual(start.GetNextWeekday(DayOfWeek.Tuesday), start.AddBusinessDays(1));

      Assert.AreEqual(start.GetNextWeekday(DayOfWeek.Friday), start.AddBusinessDays(4));
      Assert.AreEqual(4,start.GetBusinessDays(start.AddBusinessDays(4)));

      start = DateTime.Now.Date.GetNextWeekday(DayOfWeek.Monday);
      Console.WriteLine(new { start = new { start, start.DayOfWeek } });
      Assert.AreEqual(start.GetNextWeekday(DayOfWeek.Monday), start.AddBusinessDays(0));
      Assert.AreEqual(start.GetNextWeekday(DayOfWeek.Friday), start.AddBusinessDays(4));
      Assert.AreEqual(start.AddDays(1).GetNextWeekday(DayOfWeek.Monday), start.AddBusinessDays(5));

      start = DateTime.Now.Date.GetNextWeekday(DayOfWeek.Friday);
      Console.WriteLine(new { start = new { start, start.DayOfWeek } });
      Assert.AreEqual(start.GetNextWeekday(DayOfWeek.Monday), start.AddBusinessDays(1));

      var nextFriday = MathCore.GetBusinessDays(DateTime.Now, DateTime.Now.AddDays(1).GetNextWeekday(DayOfWeek.Friday));
      Assert.AreEqual(4, nextFriday);
    }
    [TestMethod]
    [TestCategory("Busness Days")]
    public void GetBusinessDays() {
      var start = DateTime.Parse("1/2/2000").GetNextWeekday(DayOfWeek.Monday);
      var end = start.GetNextWeekday(DayOfWeek.Friday);
      var bd = GetWeekdaysDiff(start, start);
      var bd2 = start.GetWorkingDays(start);
      Console.WriteLine(new { bd, bd2 });
      Assert.AreEqual(bd2, bd);

      bd = GetWeekdaysDiff(start, end);
       bd2 = start.GetWorkingDays(end);
      Console.WriteLine(new { bd, bd2 });
      Assert.AreEqual(bd2, bd);
    }
    [TestMethod]
    [TestCategory("Busness Days")]
    public void AddWeekdays() {
      var start = DateTime.Parse("1/2/2000").GetNextWeekday(DayOfWeek.Saturday);
      Console.WriteLine(new { start = new { start, start.DayOfWeek } });
      var end = start.GetNextWeekday(DayOfWeek.Friday);
      var bd = start.AddBusinessDays(5);
      var bd2 = AddWeekdays(start,5);
      Console.WriteLine(new { bd=new {bd, bd.DayOfWeek }, bd2=new {bd2, bd2.DayOfWeek } });
      Assert.AreEqual(bd2, bd);

      start = DateTime.Parse("1/2/2000").GetNextWeekday(DayOfWeek.Monday);
      bd = start.AddBusinessDays(5);
      bd2 = AddWeekdays(start, 5);
      Console.WriteLine(new { bd = new { bd, bd.DayOfWeek }, bd2 = new { bd2, bd2.DayOfWeek } });
      Assert.AreEqual(bd2, bd);
    }
    static readonly int[,] _diffOffset =
{
  // Su M  Tu W  Th F  Sa
    {0, 1, 2, 3, 4, 5, 5}, // Su
    {4, 0, 1, 2, 3, 4, 4}, // M 
    {3, 4, 0, 1, 2, 3, 3}, // Tu
    {2, 3, 4, 0, 1, 2, 2}, // W 
    {1, 2, 3, 4, 0, 1, 1}, // Th
    {0, 1, 2, 3, 4, 0, 0}, // F 
    {0, 1, 2, 3, 4, 5, 0}, // Sa
};

    public static int GetWeekdaysDiff(DateTime dtStart, DateTime dtEnd) {
      int daysDiff = (int)(dtEnd - dtStart).TotalDays;
      return daysDiff >= 0
          ? 5 * (daysDiff / 7) + _diffOffset[(int)dtStart.DayOfWeek, (int)dtEnd.DayOfWeek]
          : 5 * (daysDiff / 7) - _diffOffset[6 - (int)dtStart.DayOfWeek, 6 - (int)dtEnd.DayOfWeek];
    }
    private static readonly int[,] _addOffset =
    {
  // 0  1  2  3  4
    {0, 1, 2, 3, 4}, // Su  0
    {0, 1, 2, 3, 4}, // M   1
    {0, 1, 2, 3, 6}, // Tu  2
    {0, 1, 4, 5, 6}, // W   3
    {0, 1, 4, 5, 6}, // Th  4
    {0, 3, 4, 5, 6}, // F   5
    {0, 2, 3, 4, 5}, // Sa  6
};

    public static DateTime AddWeekdays(DateTime date, int weekdays) {
      int extraDays = weekdays % 5;
      int addDays = weekdays >= 0
          ? (weekdays / 5) * 7 + _addOffset[(int)date.DayOfWeek, extraDays]
          : (weekdays / 5) * 7 - _addOffset[6 - (int)date.DayOfWeek, -extraDays];
      return date.AddDays(addDays);
    }
  }
}