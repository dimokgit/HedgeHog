﻿using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UT = Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.Windows;
using Order2GoAddIn;
using FXW = Order2GoAddIn.FXCoreWrapper;
using HedgeHog;
using HedgeHog.Bars;

namespace TestHH {
  /// <summary>
  /// Summary description for UnitTest1
  /// </summary>
  [TestClass]
  public class UnitTest1 {
    new Order2GoAddIn.FXCoreWrapper o2g = new Order2GoAddIn.FXCoreWrapper();
    IEnumerable<Order2GoAddIn.FXCoreWrapper.Rate> bars;
    public UnitTest1() {
      //
      // TODO: Add constructor logic here
      //
    }

    private TestContext testContextInstance;

    /// <summary>
    ///Gets or sets the test context which provides
    ///information about and functionality for the current test run.
    ///</summary>
    public TestContext TestContext {
      get {
        return testContextInstance;
      }
      set {
        testContextInstance = value;
      }
    }

    #region Additional test attributes
    //
    // You can use the following additional attributes as you write your tests:
    //
    // Use ClassInitialize to run code before running the first test in the class
    // [ClassInitialize()]
    // public static void MyClassInitialize(TestContext testContext) { }
    //
    // Use ClassCleanup to run code after all tests in a class have run
    // [ClassCleanup()]
    // public static void MyClassCleanup() { }
    //
    // Use TestInitialize to run code before running each test 
    [TestInitialize()]
    public void MyTestInitialize() {
      var core = new Order2GoAddIn.CoreFX(true);
      core.LoginError += new Order2GoAddIn.CoreFX.LoginErrorHandler(core_LoginError);
      if (!core.LogOn("MICR424717001", "5890", true)) UT.Assert.Fail("Login");
      o2g = new FXCoreWrapper(core, "EUR/JPY");
    }

    void core_LoginError(Exception exc) {
      System.Windows.MessageBox.Show(exc.Message);
    }
    //
    // Use TestCleanup to run code after each test has run
    [TestCleanup()]
    public void MyTestCleanup() {
      o2g.LogOff();
    }
    //
    #endregion

    public void GetAccount() {
      var a = o2g.GetAccount();
      MessageBox.Show("PMC:" + a.PipsToMC);
    }
    public void IndicatorList() {
      Indicators.List();
    }
    public void CMA() {
      var ticks = o2g.GetTicks(5000).ToArray();
      var sw = Stopwatch.StartNew();
      var rates = ticks.ToArray().GroupTicksToRates().ToArray();
      rates.SetCMA(5);
      Debug.WriteLine("Get CMA:" + sw.Elapsed.TotalSeconds);
      SaveToFile(rates, r => r.PriceCMA[2], "C:\\CMA.csv");
    }
    public void Wave() {
      var ticks = o2g.GetTicks(5000).ToArray();
      var sw = Stopwatch.StartNew();
      var rates = ticks.ToArray().GroupTicksToRates().ToArray();
      var startDate = rates.Last().StartDate.AddMinutes(-5);
      rates.Where(r => r.StartDate < startDate).ToArray().FillFractals(TimeSpan.FromMinutes(1));
      Debug.WriteLine("Get Fractal:" + sw.Elapsed.TotalSeconds);
      sw.Reset();
      rates.FillFractals(TimeSpan.FromMinutes(1));
      Debug.WriteLine("Get Fractal:" + sw.Elapsed.TotalSeconds);
      SaveToFile(rates, r=>r.Fractal, "C:\\Wave.csv");
    }
    public void FRACTAL() {
      DateTime timer = DateTime.Now;
      Func<FXW.Rate, double?> getTsi = r => r.PriceTsi;
      Action<FXW.Rate, double?> setTsi = (r, d) => r.PriceTsi = d;
      FXW.Rate[] rates = o2g.GetBarsBase(1,DateTime.Now.AddMinutes(-41)).ToArray();
      Debug.WriteLine("Get Ticks:" + (DateTime.Now - timer).TotalSeconds);

      timer = DateTime.Now;
      rates.FillFractal(setTsi);
      Debug.WriteLine("Get Fractal:" + (DateTime.Now - timer).TotalSeconds);

      timer = DateTime.Now;
      rates.FillFractals();
      Debug.WriteLine("Get Fractal:" + (DateTime.Now - timer).TotalSeconds);

      timer = DateTime.Now;
      var f = rates.Select(r => new { r.StartDate, Fractal = r.FractalBuy + r.FractalSell }).ToArray();
      Debug.WriteLine("Get Fractal:" + (DateTime.Now - timer).TotalSeconds);

      SaveToFile(rates, getTsi, "C:\\FRACTAL.csv");
    }
    public void TSI() {
      DateTime timer = DateTime.Now;
      Func<FXW.Rate, double?> getTsi = r => r.PriceTsi;
      Action<FXW.Rate, double?> setTsi = (r, d) => r.PriceTsi = d;
      FXW.Rate[] rates = o2g.GetTicks(10000).OrderBars().ToArray();
      Debug.WriteLine("Get Ticks:" + (DateTime.Now - timer).TotalSeconds);

      timer = DateTime.Now;
      rates.FillTSI(getTsi, setTsi);
      Debug.WriteLine("Get Tsi:" + (DateTime.Now - timer).TotalSeconds);
      SaveToFile(rates, getTsi, "C:\\TSI.csv");
    }
    public void TSI_CR_M1() {
      DateTime timer = DateTime.Now;
      Func<FXW.Rate, double?> getTsi = r => r.PriceTsi;
      Action<FXW.Rate, double?> setTsi = (r, d) => r.PriceTsi = d;
      var dateStart = DateTime.Now.AddHours(-12);//.Parse("12/10/09 3:30");
      FXW.Rate[] rates = o2g.GetBarsBase(1,dateStart ).OrderBars().ToArray();
      Debug.WriteLine("Get Ticks:" + (DateTime.Now - timer).TotalSeconds);

      timer = DateTime.Now;
      rates.FillTSI_CR();
      //rates.CR(r=>(double)r.PriceTsi, (FXW.Rate r, double? d) => r.PriceTsiCR = d);
      rates.ToList().ForEach(r => r.PriceAvg1 = (double)(r.PriceTsi - r.PriceTsiCR));
      rates.FillRLW();
      Debug.WriteLine("Get Rsi:" + (DateTime.Now - timer).TotalSeconds);
      SaveToFile(rates, r => r.PriceTsi, r => r.PriceTsiCR,r=>r.PriceAvg1, "C:\\TSI_CR_M1.csv");
    }
    public void TSI_M1() {
      DateTime timer = DateTime.Now;
      Func<FXW.Rate, double?> getTsi = r => r.PriceTsi;
      Action<FXW.Rate, double?> setTsi = (r, d) => r.PriceTsi = d;
      FXW.Rate[] rates = o2g.GetBars(1, DateTime.Now.AddHours(-8)).OrderBars().ToArray();
      Debug.WriteLine("Get Ticks:" + (DateTime.Now - timer).TotalSeconds);

      timer = DateTime.Now;
      rates.FillTSI(setTsi);
      Debug.WriteLine("Get Rsi:" + (DateTime.Now - timer).TotalSeconds);
      SaveToFile(rates, getTsi, "C:\\TSI_M1.csv");
    }
    public void RSI() {
      DateTime timer = DateTime.Now;
      Func<FXW.Rate, double?> getRsi = r => r.PriceRsi;
      Action<FXW.Rate, double?> setRsi = (r, d) => r.PriceRsi = d;
      FXW.Rate[] rates = o2g.GetBarsBase(1,DateTime.Parse("12/3/09 00:00"),DateTime.FromOADate(0)).OrderBars().ToArray();
      Debug.WriteLine("Get Ticks:" + (DateTime.Now - timer).TotalSeconds);

      rates.FillRSI(14, r => r.PriceClose, setRsi);
      SaveToFile(rates, getRsi, "C:\\RSI_M1.csv");
      Debug.WriteLine("StdDev:" + rates.StdDev(getRsi));
      Debug.WriteLine("Get Rsi:" + (DateTime.Now - timer).TotalSeconds);
    }
    public void Fractals() {
      DateTime timer = DateTime.Now;
      FXW.Rate[] rates = o2g.GetBars(1,DateTime.Now.AddHours(-8)).ToArray();
      Debug.WriteLine("Get Ticks:" + (DateTime.Now - timer).TotalSeconds);
      Func<FXCoreWrapper.Rate , double> getFractal = t => t.FractalBuy > 0 ? -1 : t.FractalSell > 0 ? 1 : 0;
      timer = DateTime.Now;
      rates.FillFractal();
      Debug.WriteLine("Get Fractal:" + (DateTime.Now - timer).TotalSeconds);
      SaveToFile(rates, getFractal, "C:\\Fractal.csv");
    }
    public void StdDev() {
      DateTime timer = DateTime.Now;
      Func<FXW.Rate, double?> getRsi = r => r.PriceRsi;
      Action<FXW.Rate, double?> setRsi = (r, d) => r.PriceRsi = d;
      FXW.Rate[] rates = o2g.GetTicks(10000).ToArray().GroupTicksToRates().OrderBars().ToArray();
      Debug.WriteLine("Get Ticks:" + (DateTime.Now-timer).TotalSeconds);

      timer = DateTime.Now;
      rates.FillRSI(14, r => r.PriceClose, getRsi, setRsi);
      SaveToFile(rates, getRsi, "C:\\RSI.csv");
      Debug.WriteLine("StdDev:" + rates.StdDev(getRsi));
      Debug.WriteLine("Get Rsi:" + (DateTime.Now - timer).TotalSeconds);
    }
    [TestMethod]
    public void GetRSI() {
      var rates = o2g.GetTicks(10000).ToArray();
      var t = DateTime.Now;
      var dd = rates.FillRsi(14, r => r.PriceClose);// Indicators.RSI(rates, r => r.AskClose, 14);
      System.Diagnostics.Debug.WriteLine((DateTime.Now - t).TotalSeconds + " ms");
      SaveToFile(rates, r=>r.PriceRsi, "C:\\RSI.csv");
    }
    void SaveToFile<T, D>(IEnumerable<T> rates, Func<T, D> price, Func<T, D> price1, Func<T, D> price2, string fileName) where T : FXW.Rate {
      StringBuilder sb = new StringBuilder();
      sb.Append("Time,Price,Indicator,Indicator1,Indicator2" + Environment.NewLine);
      rates.ToList().ForEach(r => sb.Append(r.StartDate + "," + r.PriceClose + "," + price(r) + "," + price1(r) + "," + price2(r) + Environment.NewLine));
      using (var f = System.IO.File.CreateText(fileName)) {
        f.Write(sb.ToString());
      }
    }
    void SaveToFile<T, D>(IEnumerable<T> rates, Func<T, D> price, Func<T, D> price1, string fileName) where T : FXW.Rate {
      StringBuilder sb = new StringBuilder();
      sb.Append("Time,Price,Indicator,Indicator1" + Environment.NewLine);
      rates.ToList().ForEach(r => sb.Append(r.StartDate + "," + r.PriceClose + "," + price(r) + "," + price1(r) + Environment.NewLine));
      using (var f = System.IO.File.CreateText(fileName)) {
        f.Write(sb.ToString());
      }
    }
    void SaveToFile<T, D>(IEnumerable<T> rates, Func<T, D> price, string fileName) where T : FXW.Rate {
      StringBuilder sb = new StringBuilder();
      sb.Append("Time,Price,Indicator" + Environment.NewLine);
      rates.ToList().ForEach(r => sb.Append(r.StartDate.ToShortDateString() + " " + r.StartDate.ToLongTimeString() + "," + r.PriceClose + "," + price(r) + Environment.NewLine));
      using (var f = System.IO.File.CreateText(fileName)) {
        f.Write(sb.ToString());
      }
    }
    public void GetRLW() {
      var rates = o2g.GetTicks(15000).OrderBars().ToArray();
      rates.ToList().ForEach(r => r.PriceAvg4 = -101);
      var time = DateTime.Now;
      var peaks = new []{0.0,-100.0};
      FillRLW_RSI(rates, 14, rates.First().StartDate.AddMinutes(14), rates.Last().StartDate
        , (t, p) => t.PriceAvg2 = p, (t, p) => t.PriceAvg3 = p, (t, p) => t.PriceAvg4 = p);
      Func<double, bool> IsRlwSell = (rlw) => rlw > -.5;
      Func<double, bool> IsRlwBuy = (rlw) => rlw.Between(-100, -99.5);
      Func<double, double, bool> IsRsiBuy = (rsi, offset) => rsi + offset <= 30;
      Func<double, double, bool> IsRsiSell = (rsi, offset) => rsi + offset >= 70;
      int? rlwBalanceBuy = null;
      int? rlwBalanceSell = null;
      DateTime rlwTimeBuy = DateTime.MinValue;
      DateTime rlwTimeSell = DateTime.MinValue;
      DateTime rsiTimeBuy = DateTime.MaxValue;
      DateTime rsiTimeSell = DateTime.MaxValue;
      rates./*Where(r=>peaks.Contains(r.PriceAvg4)).*/ToList()
        .ForEach(p => {
          var rlw = p.PriceAvg4;
          var rsi = p.PriceAvg2;
          if (IsRlwBuy(rlw) && !rlwBalanceBuy.HasValue) rlwBalanceBuy = 0;
          if (IsRlwSell(rlw) && !rlwBalanceSell.HasValue) rlwBalanceSell= 0;
          var goBuy = false;
          if (rlwBalanceBuy.HasValue) {
            if (rlwBalanceBuy > 0) {
              goBuy = true;
              rlwBalanceBuy = null;
              rlwTimeBuy = p.StartDate;
            } else rlwBalanceBuy = rlwBalanceBuy + (IsRlwBuy(rlw) ? -1 : 1);
          }
          var goSell = false;
          if (rlwBalanceSell.HasValue) {
            if (rlwBalanceSell > 0) {
              goSell = true;
              rlwBalanceSell = null;
              rlwTimeSell = p.StartDate;
            } else rlwBalanceSell = rlwBalanceSell + (IsRlwSell(rlw) ? -1 : 1);
          }
          if (IsRlwBuy(rlw)) rlwTimeBuy = p.StartDate;
          if (IsRlwSell(rlw)) rlwTimeSell = p.StartDate;
          if (IsRsiBuy(rsi, 0)) rsiTimeBuy = p.StartDate;
          if (IsRsiSell(rsi,0)) rsiTimeSell = p.StartDate;

          Func<DateTime, DateTime, DateTime, bool> goTradeLambda = (tRlw, tRsi, tNow) => 
            Math.Abs((tRlw - tRsi).TotalSeconds) < 15 && (tNow - new[] { tRsi, tRlw }.Max()).TotalSeconds < 15;

          Debug.WriteLine(string.Format("{0:HH:mm:ss} {1:n3} {2:n1} {3:n0} {4} {5} {6}"
            , p.StartDate, p.PriceClose, p.PriceAvg4, p.PriceAvg2
            , goBuy ? "RlwBuy" : goSell ? "RlwSell" : ""
            , IsRsiBuy(rsi,0) ? "RsiBuy" : IsRsiSell(rsi,0) ? "RsiSell" : ""
            , goTradeLambda(rlwTimeBuy, rsiTimeBuy, p.StartDate) ? " Buy:" : goTradeLambda(rlwTimeSell, rsiTimeSell, p.StartDate) ? " Sell" : ""
            //, IsRlwBuy(rlw) || IsRlwSell(rlw) || IsRsiBuy(rsi, 0) || IsRsiSell(rsi, 0) ? " Trade:" : ""
           ));
        });
      return;
      var points = rates.Select(t =>
        Indicators.RLW(rates
        .Where(t1 => t1.StartDate.Between(t.StartDate, t.StartDate.AddMinutes(15)))
        .ToArray().GetMinuteTicks(1).ToArray()).Last()
      );
      points.OrderBy(p => p.Time).ToList().ForEach(p =>System.Diagnostics.Debug.WriteLine(p));
    }

    //void FillRSI_(IEnumerable<FXW.Tick> ticks, int period, Func<FXW.Rate, double> priceSource, Func<FXW.Rate, double> priceDestination, Action<FXW.Rate, double> priceRsi) {
    //  var startDate = ticks.Where(t => priceDestination(t) > 0).Max(r => r.StartDate).AddMinutes(-period-2);
    //  FillRSI(ticks.Where(r => r.StartDate >= startDate), period, priceSource, priceRsi);
    //}
    void FillRLW_RSI(FXW.Tick[] ticks, int period, DateTime dateStart, DateTime dateEnd
      , Action<FXW.Rate, double> priceRsi, Action<FXW.Rate, double> priceRsiCR, Action<FXW.Rate, double> priceRlw) {
      ticks.Where(t => t.StartDate > dateStart).ToList().ForEach(t =>{
        var ticksLocal = ticks
        .Where(t1 => t1.StartDate.Between(t.StartDate.AddMinutes(-period - 2), t.StartDate))
        .ToArray().GetMinuteTicks(1);
        var dp = Indicators.RSI_CR(ticksLocal, p => p.PriceClose, period).Last();
        priceRsi(t, dp.Point);
        priceRsiCR(t, dp.Point1);
        priceRlw(t, Indicators.RLW(ticksLocal.ToArray(), period).Last().Point);
      }
      );
    }
    public void ShowTables() {
      return;
      Action<FXCore.TableAut,int> showTable = (t,i) => {
        var r = (t.Rows as FXCore.RowsEnumAut).Item(i) as FXCore.RowAut;
        foreach (FXCore.ColumnAut c in t.Columns as FXCore.ColumnsEnumAut) {
          Debug.WriteLine(c.Title+":"+r.CellValue(c.Title));
        }
      };
      var table = o2g.GetTable(FXW.TABLE_ACCOUNTS);
      showTable(table, 1);
      Debug.WriteLine("");
      table = o2g.GetTable(FXW.TABLE_TRADES);
      showTable(table, 1);
      Debug.WriteLine("");
      table = o2g.GetTable(FXW.TABLE_ORDERS);
      showTable(table, 3);
      Debug.WriteLine("");
      showTable(o2g.GetTable(FXW.TABLE_SUMMARY), 3);
      //var account = o2g.GetAccount();
    }
    public void GetTicksTest() {
      DateTime d = DateTime.Now;
      var ticks = o2g.GetTicks(5000, DateTime.Now.AddDays(-1), DateTime.FromOADate(0));
      Debug.WriteLine("Ticks:" + ticks.Count() + ",From:" + ticks.Min(t => t.StartDate) + " To:" + ticks.Max(t => t.StartDate));
      Debug.WriteLine("Ticks Time:"+(DateTime.Now-d).TotalMilliseconds);
      d = DateTime.Now;
      var rates = ticks.ToArray().GetMinuteTicks(5);
      Debug.WriteLine("Rates Time:" + (DateTime.Now - d).TotalMilliseconds);
      Debug.WriteLine("Rates Spread:" + rates.Average(r=>r.Spread));

      foreach (var rate in rates) {
        foreach (var p in rate.GetType().GetProperties())
          System.Diagnostics.Debug.WriteLine(p.Name + ":" + p.GetValue(rate, new object[] { }));

      }
    }
    public void TestMethod2() {
      var volts = HedgeHog.Signaler.GetVoltageByTick(bars, 15);
      var s = "";
      volts.ForEach(v => s += v.StartDate + "," + v.Volts + "," + v.Price+","+v.PriceAvg + Environment.NewLine);
      System.IO.File.WriteAllText("C:\\Volts.csv", s);
      System.Diagnostics.Debug.WriteLine("Volts:" + volts.Count());
      return;
    }
  }
}