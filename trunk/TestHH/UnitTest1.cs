using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Data.Objects.DataClasses;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UT = Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.Windows;
using Order2GoAddIn;
using FXW = Order2GoAddIn.FXCoreWrapper;
using HedgeHog;
using HedgeHog.Bars;
using HedgeHog.Shared;
using HedgeHog.Rsi;
using System.Windows.Data;
using System.Threading;
using System.IO;
using System.Xml.Linq;

namespace TestHH {
  /// <summary>
  /// Summary description for UnitTest1
  /// </summary>
  [TestClass]
  public class UnitTest1 {
    new Order2GoAddIn.FXCoreWrapper o2g = new Order2GoAddIn.FXCoreWrapper(new CoreFX());
    IEnumerable<Rate> bars;
    public UnitTest1() {    }

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
    CoreFX core = new Order2GoAddIn.CoreFX(true);
    [TestInitialize()]
    public void MyTestInitialize() {
      core.LoginError += new Order2GoAddIn.CoreFX.LoginErrorHandler(core_LoginError);
      o2g = new FXCoreWrapper(core, "EUR/USD");

      //if (!core.LogOn("6519040180", "Tziplyonak713", false)) UT.Assert.Fail("Login");
      //if (!core.LogOn("6519048070", "Toby2523", false)) UT.Assert.Fail("Login");
      //if (!core.LogOn("MICR485510001", "9071", true)) UT.Assert.Fail("Login");
      if (!core.LogOn("FX1179853001", "8041", true)) UT.Assert.Fail("Login");
      o2g.OrderRemoved += new FXW.OrderRemovedEventHandler(o2g_OrderRemovedEvent);
    }

    void o2g_OrderRemovedEvent(Order order) {
      Debug.WriteLine("Order remover with status:" + order.FixStatus);
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

    [TestMethod]
    public void LoadTradeFromXml() {
      var xmlString = File.ReadAllText(@"C:\Data\Dev\Forex_Old\Projects\HedgeHog\HedgeHog.Alice.Client\bin\Debug_03\ClosedTrades.txt");
      var x = XElement.Parse("<x>" + xmlString + "</x>");
      var nodes = x.Nodes().ToArray();
      foreach (XElement node in nodes.Reverse().Take(150)) {
        var trade = new Trade();
        trade.FromString(node);
        Debug.WriteLine(trade);
      }

    }
    public void MunuteRsi(){
      var minutesBack = 60*24*2;
      var rates = o2g.GetBarsBase(o2g.Pair, 1,DateTime.Now.AddMinutes(-minutesBack)).ToArray();
      rates.FillRsis((minutesBack * 0.5).ToInt());
      var statName = "MinuteRsi";
      var context = new ForexEntities();
      var a = typeof(t_Stat).GetCustomAttributes(typeof(EdmEntityTypeAttribute), true).Cast<EdmEntityTypeAttribute>();
      context.ExecuteStoreCommand("DELETE " + a.First().Name + " WHERE Name={0}", statName);
      var stats = context.t_Stat;
      rates.Where(t => t.PriceRsi.GetValueOrDefault(50)!=50).ToList().ForEach(t =>
        stats.AddObject(new t_Stat() {
          Time = t.StartDate, Name = statName, Price = t.PriceAvg,
          Value1 = 0,
          Value2 = 0,
          Value3 = t.PriceRsi.Value
        }));
      context.SaveChanges();
    }
    public void Legerages() {
      o2g.GetOffers().Select(o => o.Pair).ToList().ForEach(p => Debug.WriteLine("{0}:{1:n2}", p, o2g.Leverage(p)));
    }
    public void GetTradingSettings() {
      Debug.WriteLine(o2g.GetTradingSettings("USD/JPY").PropertiesToString(Environment.NewLine));
    }
    public void GetOrders() {
      var orders = o2g.GetOrders("");
      string orderID = "", tradeID = "";
      var toc = o2g.Desk.GetTimeout(o2g.Desk.TIMEOUT_COMMON);
      var pair = "USD/JPY";
      var price = o2g.GetOffers().First(o => o.Pair == pair).Ask;
      o2g.FixOrderOpen("USD/JPY", true, 1000, price + o2g.InPoints(pair, 15), price - o2g.InPoints(pair, 15), "Dimok");
      var t = new Thread(() => Thread.Sleep(5000));
      t.Start();
      t.Join();
      if (tradeID != "")
        o2g.FixOrdersClose(tradeID);
      t = new Thread(() => Thread.Sleep(5000));
      ListCollectionView List = new ListCollectionView(orders);
    }

    void ServerTime() {
      var sw = Stopwatch.StartNew();
      var st = o2g.ServerTime;
      sw.Stop();
      Debug.WriteLine("ServerTime:{0:HH:mm:ss},LocalTime:{1:HH:mm:ss},Diff:{3},StopWatch:{2} ms", st, DateTime.Now, sw.ElapsedMilliseconds, (st - DateTime.Now).TotalSeconds);
      return;
    }

    public void Waves1() {
      var statName = System.Reflection.MethodBase.GetCurrentMethod().Name;
      var dateStart = DateTime.Parse("07/20/2009 12:00");
      var dateEnd = dateStart.AddHours(0.5);
      var context = new ForexEntities();
      var a = typeof(t_Stat).GetCustomAttributes(typeof(EdmEntityTypeAttribute), true).Cast<EdmEntityTypeAttribute>();
      context.ExecuteStoreCommand("DELETE " + a.First().Name + " WHERE Name={0}", statName);

      var ticks = o2g.GetTicks(o2g.Pair, 12000);
      var t1 = o2g.GetTicks(o2g.Pair, 900);
      ticks = ticks.Union(t1).OrderBars().ToArray();
      ticks.ToList().ForEach(t => t.StartDate = t.StartDate.AddMilliseconds(-t.StartDate.Millisecond));
      var rates = ticks.Cast<Rate>().OrderBars().ToArray();

      Stopwatch timer = Stopwatch.StartNew();
      foreach (var tick in rates.OrderBarsDescending())
        tick.Trend.Volume = rates.Where(TimeSpan.FromMinutes(1), tick).Count();
      Debug.WriteLine("Volumes:" + timer.Elapsed);

      var rsiTicks = 250;
      var rsiPeriod = TimeSpan.FromMinutes(14);

      timer = Stopwatch.StartNew();
      rates.FillRsis(rsiTicks);
      Debug.WriteLine("FillRsi 1:" + timer.Elapsed);

      timer = Stopwatch.StartNew();
      rates.Rsi(rsiTicks, (r, v) => r.PriceAvg1 = (double)v,r=>r.PriceAvg1);
      Debug.WriteLine("FillRsi 2:" + timer.Elapsed);

      timer = Stopwatch.StartNew();
      var rsiStats = rates.RsiStats();
      Debug.WriteLine("RsiStats:" + timer.Elapsed + " " + string.Format("{0:n0}/{1:n0}", rsiStats.Sell, rsiStats.Buy));


      timer = Stopwatch.StartNew();
      rates.SkipWhile(t => !t.PriceRsi.HasValue).SetCMA(t => t.PriceRsi.GetValueOrDefault(), 2);
      Debug.WriteLine("CMA:" + timer.Elapsed);


      //var ticks = context.v_Tick.Where(t => t.StartDate >= dateStart && t.StartDate <= dateEnd)
      //  .ToArray().Select((t, i) => 
      //    new Tick(t.StartDate, t.Ask, t.Bid, i, true) { Trend = new BarBase.TrendInfo() { Volume = t.Count.Value } }).ToArray();

      var stats = context.t_Stat;
      rates.Where(t => t.PriceRsi.HasValue && !double.IsNaN(t.PriceRsi.Value)).ToList().ForEach(t =>
        stats.AddObject(new t_Stat() {
          Time = t.StartDate, Name = statName, Price = t.PriceAvg
            , Value1 = t.PriceAvg1
            , Value2 = 0
          ,
          Value3 = t.PriceRsi.Value
        }));
      context.SaveChanges();


      //timer = Stopwatch.StartNew();
      //var waves = ticks.FindWaves(3);
      //Debug.WriteLine("FindWaves(3):"+timer.Elapsed);

      //waves.ToList().ForEach(w => Debug.WriteLine(w));
    }

    public void GetAccount() {
      var a = o2g.GetAccount();
      MessageBox.Show("PMC:" + a.PipsToMC);
    }
    public void GetTrades() {
      var account = o2g.GetAccount();
      Debug.WriteLine("StopAmount:{0}", account.StopAmount);
      var aID = account.ID;
      var pair = "USD/JPY";
      var baseUnitSize = o2g.MinimumQuantity;
      var pipCost = o2g.GetPipCost(pair);
      var lots = 10000;
      var pips = 10;
      MessageBox.Show(pips + " pips cost for " + lots + " of " + pair + " = " + FXW.PipsAndLotToMoney(pips, lots, pipCost, baseUnitSize));
      o2g.GetTrades("").Where(t=>t.Stop>0).ToList()
        .ForEach(t => Debug.WriteLine("Id:{0},Stop:{1}", t.Id, t.Stop));
    }
    public void GetOffers() {
      var aID = o2g.GetAccount().ID;
      dynamic c = o2g.Desk.TradingSettingsProvider;
      List<string> str = new List<string>();
      o2g.GetOffers().ToList().ForEach(o => str.Add(string.Format("Pair:{0} - MMR:{1},BU:{2},", o.Pair, o.MMR, c.GetBaseUnitSize("EUR/USD", aID))));
    }

    public void IndicatorList() {
      Indicators.List();
    }
    public void Speed() {
      var dateStart = DateTime.Parse("04/08/2010 08:00");
      var dateEnd = dateStart.AddHours(4);
      var ticks = o2g.GetBarsBase(o2g.Pair, 0, dateStart, dateEnd).Cast<Tick>().GroupTicksToRates().ToArray();
      var rates = ticks.GetMinuteTicks(1);
      rates.OrderBarsDescending().FillOverlaps(TimeSpan.FromMinutes(1));
      int np = 4;
      var period = TimeSpan.FromMinutes(6);
      var tickToFill = ticks.SkipWhile(t => t.StartDate.Between(ticks.First().StartDate, ticks.First().StartDate + period.Multiply(np))).ToArray();
      foreach (var tick in tickToFill) {
        var to = rates.Where(period.Multiply(np), tick).ToArray();
        var ols = to.Select(t => t.Overlap).ToArray();
        period = ols.Average().Multiply(2);
        ticks.Where(period, tick).ToArray().FillSpeed(tick, t => t.PriceAvg);
      }
      tickToFill.SetCMA(t => t.PriceSpeed.Value, 20);
      period = TimeSpan.FromMinutes(6);
      tickToFill = ticks.SkipWhile(t => t.PriceCMA==null).SkipWhile(t => t.StartDate.Between(ticks.First().StartDate, ticks.First().StartDate + period.Multiply(np))).ToArray();
      foreach (var tick in tickToFill) {
        var to = rates.Where(period.Multiply(np), tick).ToArray();
        var ols = to.Select(t => t.Overlap).ToArray();
        period = ols.Average().Multiply(2);
        ticks.SkipWhile(t => t.PriceCMA == null).Where(period, tick).ToArray().FillSpeed(tick, t => t.PriceCMA[0]);
      }
      ticks.Where(t => t.PriceSpeed.HasValue).SaveToFile(
         r => o2g.InPips(1, r.PriceSpeed),
         r => o2g.InPips(1, r.PriceCMA[0]),
         r => o2g.InPips(1, r.PriceCMA[2]),
         "C:\\Speed.csv");
    }
    public void TicksPerMinute() {
      var dateFrom = DateTime.Parse("4/2/2010 10:13:16");
      var dateTo = DateTime.Parse("4/2/2010 10:33:27");
      var ticks = o2g.GetTicks(o2g.Pair, 13000).Where(dateFrom, dateTo).ToArray();
      //var ticks = new List<Rate>();
      //o2g.GetBars(0,dateFrom,dateTo,ref ticks);
      ticks.FillMass();
      Debug.WriteLine("TicksPerMinute:{0: HH:mm:ss} -{1: HH:mm:ss}={2:n0}/{3}", ticks.First().StartDate, ticks.Last().StartDate,
        ticks.TradesPerMinute(), ticks.SumMass());
    }
    public void LoadTicks() {
      var ticks = o2g.GetTicks(o2g.Pair, 1200000).OrderBarsDescending().ToArray();
      using (var context = new ForexEntities() { CommandTimeout = 600 }) {
        var lastDate = ticks.Min(t => t.StartDate);
        var a = typeof(t_Tick).GetCustomAttributes(typeof(EdmEntityTypeAttribute), true).Cast<EdmEntityTypeAttribute>();
        context.ExecuteStoreCommand("DELETE " + a.First().Name + " WHERE StartDate>={0}", lastDate);
        var t_ticks = context.t_Tick;
        ticks.ToList().ForEach(t => {
          var tick = context.CreateObject<t_Tick>();
          tick.Pair = o2g.Pair;
          tick.StartDate = t.StartDate;
          tick.Ask = t.AskOpen;
          tick.Bid = t.BidOpen;
          context.AddTot_Tick(tick);
        });
        context.SaveChanges(System.Data.Objects.SaveOptions.DetectChangesBeforeSave);
      }
    }

    public void Waves() {
      var dateStart = DateTime.Parse("03/14/2010 16:00");
      dateStart = DateTime.Now.AddHours(-2);
      //var ticks = o2g.GetBarsBase(0, dateStart, DateTime.FromOADate(0)).Cast<Tick>().OrderBarsDescending().ToArray();
      var ticks = o2g.GetTicks(o2g.Pair, 12000).OrderBarsDescending().ToArray();
      ticks.FillMass();
      Stopwatch timer = Stopwatch.StartNew();
      var period = ticks.Count() / (ticks.Max(t => t.StartDate) - ticks.Min(t => t.StartDate)).Duration().TotalMinutes;
      var waves0 = ticks.GetWaves(period.ToInt());
      var waves = waves0.OrderBy(w=>w).Take(waves0.Count() - 1).ToArray();
      var wa = waves.Average();
      var wst = waves.StdDev();
      waves = waves.Where(w => w > wa).ToArray();
      if (waves != null && waves.Count() > 1) {
        Debug.WriteLine("Wave Avg:" + o2g.InPips(wa, 1));
        Debug.WriteLine("Wave StDev:" + o2g.InPips(wst, 1));
        Debug.WriteLine("Wave Average:" + o2g.InPips(waves.Average(), 1));
        waves0.ToList().ForEach(w => Debug.WriteLine("w:" + o2g.InPips(w, 1)));
        Debug.WriteLine("ticks.GetWaves:" + timer.Elapsed.TotalSeconds + " sec."); timer.Reset(); timer.Start();
        var ws = waves0.GetWaveStats();
        Debug.WriteLine("Wave Avg:" + o2g.InPips(ws.Average, 1));
        Debug.WriteLine("Wave StDev:" + o2g.InPips(ws.StDev, 1));
        Debug.WriteLine("Wave AverageN:" + o2g.InPips(ws.AverageUp, 1));
      }
    }


    public void Mass() {
      var dateStart = DateTime.Parse("03/14/2010 16:00");
      dateStart = DateTime.Now.AddHours(-4);
      var ticks = o2g.GetBarsBase(o2g.Pair, 0, dateStart, DateTime.FromOADate(0)).Cast<Tick>().OrderBarsDescending().ToArray();
      //var ticks = o2g.GetTicks(12000).OrderBarsDescending().ToArray();
      ticks.FillMass();
      var waveHeight = 0.00053;
      var wavePeriod = TimeSpan.FromMinutes(6);
      Stopwatch timer = Stopwatch.StartNew();
      var fractals = ticks.FindFractals(waveHeight, wavePeriod, 1, 100)
        .Concat(new[] { ticks.First().Clone() as Tick }).OrderBarsDescending().ToArray();
      ticks.FillPower(1.0.FromMinutes());
      Debug.WriteLine("Ticks.FindFractals:" + timer.Elapsed.TotalSeconds + " sec."); timer.Reset(); timer.Start();

      ticks.SetCMA(t => t.Ph.Work.Value, 100);
      ticks.Where(t => t.Ph.Power.HasValue).OrderBars().SaveToFile(
         r => o2g.InPips(2, r.Ph.Work.Value)
        , r => o2g.InPips(2, r.PriceCMA[0])
        , r => o2g.InPips(2, r.PriceCMA[2])
        , "C:\\WorkCMA.csv");

      ticks.SetCMA(t => t.Ph.Power.Value, 100);
      ticks.Where(t => t.Ph.Power.HasValue).OrderBars().SaveToFile(
          r => o2g.InPips(2, r.Ph.Power.Value)
        , r => o2g.InPips(2, r.PriceCMA[0])
        , r => o2g.InPips(2, r.PriceCMA[2])
        , "C:\\PowerCMA.csv");

      ticks.OrderBars().ToArray().RunTotal(t => t.Ph.Power);
      ticks.Where(t => t.Ph.Power.HasValue).OrderBars().SaveToFile(
         r => o2g.InPips(2, r.Ph.Power.Value)
        , r => o2g.InPips(2, r.RunningTotal.Value)
        , "C:\\PowerTotal.csv");

      ticks.ToList().ForEach(t => t.RunningTotal = null);
      ticks.OrderBars().ToArray().RunTotal(t => t.Ph.Work);
      SaveToFile(ticks.Where(t => t.Ph.Power.HasValue).OrderBars()
        , r => o2g.InPips(2, r.RunningTotal.Value)
        , "C:\\WorkTotal.csv");

      SaveToFile(ticks.Where(t => t.Ph.Power.HasValue).OrderBars()
  , r => Math.Abs(r.Ph.Density.Value)
  , "C:\\Density.csv");

      //????????ticks.FillPower(fractals);
      if (fractals.Count() == 0) {
        ticks.FillPower(TimeSpan.FromMinutes(1));
        Debug.WriteLine("Ticks.FillPower:" + timer.Elapsed.TotalSeconds + " sec."); timer.Reset(); timer.Start();
        //SaveToFile(ticks.Where(t => t.Ph.Power.HasValue).OrderBars(), r => r.Ph.Density.Value, "C:\\Power.csv");
      }
      var s = new List<string>(new[] { "fractal,height,mass,time,Work,Power,Speed,KE" });

      foreach (var fractal in fractals.Take(fractals.Count() - 1)) {
        var line = new List<object>();
        line.Add(fractal);
        line.Add(o2g.InPips(fractal.Ph.Height.Value, 1));
        line.Add(o2g.InPips(fractal.Ph.Mass.Value, 1));
        line.Add(fractal.Ph.Time);
        line.Add(o2g.InPips(o2g.InPips(fractal.Ph.Work.Value), 1));
        line.Add(o2g.InPips(o2g.InPips(fractal.Ph.Power.Value), 1));
        line.Add(o2g.InPips(fractal.Ph.Speed.Value));
        line.Add(o2g.InPips(o2g.InPips(o2g.InPips(fractal.Ph.K.Value))));
        s.Add(string.Join(",", line.Select(l => l + "")));
      }
      s.Add(fractals.Last() + "");
      System.IO.File.WriteAllText("C:\\Phycisc.csv", string.Join(Environment.NewLine, s.ToArray()));
      var waves = ticks.GetWaves(60 * 5);
      if (waves != null && waves.Count() > 1) {
        Debug.WriteLine("Wave Average:" + o2g.InPips(waves.Average(), 1));
        Debug.WriteLine("ticks.GetWaves:" + timer.Elapsed.TotalSeconds + " sec."); timer.Reset(); timer.Start();
      }
    }
    public void Overlaps() {
      Stopwatch timer = Stopwatch.StartNew(); timer.Start();
      var rates = o2g.GetBarsBase(o2g.Pair, 1, DateTime.Now.AddHours(-1), DateTime.FromOADate(0)).ToArray();
      Debug.WriteLine("Get Ticks:" + timer.Elapsed.TotalSeconds+" sec.");
      timer.Reset(); timer.Start();
      rates.OrderBarsDescending().FillOverlaps(TimeSpan.FromMinutes(1));
      Debug.WriteLine("Get Overlap:" + timer.Elapsed.TotalSeconds + " sec.");
      var stDevSeconds = rates.StdDev(r => r.Overlap.TotalSeconds);
      var avgSeconds = rates.Average(r => r.Overlap.TotalSeconds);
      Debug.WriteLine("stDev:" + TimeSpan.FromSeconds(stDevSeconds).TotalMinutes);
      Debug.WriteLine("average:" + TimeSpan.FromSeconds(avgSeconds).TotalMinutes);
      Debug.WriteLine("Wave Overlap:" + TimeSpan.FromSeconds(stDevSeconds * 2 + avgSeconds).TotalMinutes);

      var overlaps = rates.Select(r => new { Date = r.StartDate, Over = r.Overlap }).ToArray();
      //SaveToFile(rates, getRsi, "C:\\RSI_M1.csv");
      //Debug.WriteLine("StdDev:" + rates.StdDev(getRsi));
      //Debug.WriteLine("Get Rsi:" + (DateTime.Now - timer).TotalSeconds);
    }
    public void CmaBars() {
      var rates = o2g.GetBars(o2g.Pair, 1, DateTime.Now.AddMinutes(-320));
      var sw = Stopwatch.StartNew();
      rates.SetCMA(1);
      Debug.WriteLine("Get CMABars:" + sw.Elapsed.TotalSeconds);
      SaveToFile(rates, r => r.PriceCMA[2], "C:\\CMA.csv");
    }
    public void CMA() {
      var ticks = o2g.GetTicks(o2g.Pair, 5000).ToArray();
      var sw = Stopwatch.StartNew();
      var rates = ticks.ToArray().GroupTicksToRates().ToArray();
      rates.SetCMA(5);
      Debug.WriteLine("Get CMA:" + sw.Elapsed.TotalSeconds);
      SaveToFile(rates, r => r.PriceCMA[2], "C:\\CMA.csv");
    }
    public void Wave() {
      var ticks = o2g.GetTicks(o2g.Pair, 5000).ToArray();
      var sw = Stopwatch.StartNew();
      var rates = ticks.ToArray().GroupTicksToRates().ToArray();
      var startDate = rates.Last().StartDate.AddMinutes(-5);
      rates.Where(r => r.StartDate < startDate).ToArray().FindFractals(0, TimeSpan.FromMinutes(1), 1, 100);
      Debug.WriteLine("Get Fractal:" + sw.Elapsed.TotalSeconds);
      sw.Reset();
      rates.FindFractals(0, TimeSpan.FromMinutes(1), 1, 100);
      Debug.WriteLine("Get Fractal:" + sw.Elapsed.TotalSeconds);
      SaveToFile(rates, r=>r.Fractal, "C:\\Wave.csv");
    }
    public void FRACTAL() {
      DateTime timer = DateTime.Now;
      Func<Rate, double?> getTsi = r => r.PriceTsi;
      Action<Rate, double?> setTsi = (r, d) => r.PriceTsi = d;
      Rate[] rates = o2g.GetBarsBase(o2g.Pair, 1, DateTime.Now.AddMinutes(-41)).ToArray();
      Debug.WriteLine("Get Ticks:" + (DateTime.Now - timer).TotalSeconds);

      timer = DateTime.Now;
      rates.FillFractal(setTsi);
      Debug.WriteLine("Get Fractal:" + (DateTime.Now - timer).TotalSeconds);

      timer = DateTime.Now;
      rates.FillFractals();
      Debug.WriteLine("Get Fractal:" + (DateTime.Now - timer).TotalSeconds);

      timer = DateTime.Now;
      Debug.WriteLine("Get Fractal:" + (DateTime.Now - timer).TotalSeconds);

      SaveToFile(rates, getTsi, "C:\\FRACTAL.csv");
    }
    public void TSI() {
      DateTime timer = DateTime.Now;
      Func<Rate, double?> getTsi = r => r.PriceTsi;
      Action<Rate, double?> setTsi = (r, d) => r.PriceTsi = d;
      Rate[] rates = o2g.GetTicks(o2g.Pair, 10000).OrderBars().ToArray();
      Debug.WriteLine("Get Ticks:" + (DateTime.Now - timer).TotalSeconds);

      timer = DateTime.Now;
      rates.FillTSI(getTsi, setTsi);
      Debug.WriteLine("Get Tsi:" + (DateTime.Now - timer).TotalSeconds);
      SaveToFile(rates, getTsi, "C:\\TSI.csv");
    }
    public void TSI_CR_M1() {
      DateTime timer = DateTime.Now;
      Func<Rate, double?> getTsi = r => r.PriceTsi;
      Action<Rate, double?> setTsi = (r, d) => r.PriceTsi = d;
      var dateStart = DateTime.Now.AddHours(-12);//.Parse("12/10/09 3:30");
      Rate[] rates = o2g.GetBarsBase(o2g.Pair, 1, dateStart).OrderBars().ToArray();
      Debug.WriteLine("Get Ticks:" + (DateTime.Now - timer).TotalSeconds);

      timer = DateTime.Now;
      rates.FillTSI_CR();
      //rates.CR(r=>(double)r.PriceTsi, (FXW.Rate r, double? d) => r.PriceTsiCR = d);
      rates.ToList().ForEach(r => r.PriceAvg1 = (double)(r.PriceTsi - r.PriceTsiCR));
      rates.FillRLW();
      Debug.WriteLine("Get Rsi:" + (DateTime.Now - timer).TotalSeconds);
      rates.SaveToFile(r => r.PriceTsi, r => r.PriceTsiCR, r => r.PriceAvg1, "C:\\TSI_CR_M1.csv");
    }
    public void TSI_M1() {
      DateTime timer = DateTime.Now;
      Func<Rate, double?> getTsi = r => r.PriceTsi;
      Action<Rate, double?> setTsi = (r, d) => r.PriceTsi = d;
      Rate[] rates = o2g.GetBars(o2g.Pair, 1, DateTime.Now.AddHours(-8)).OrderBars().ToArray();
      Debug.WriteLine("Get Ticks:" + (DateTime.Now - timer).TotalSeconds);

      timer = DateTime.Now;
      rates.FillTSI(setTsi);
      Debug.WriteLine("Get Rsi:" + (DateTime.Now - timer).TotalSeconds);
      SaveToFile(rates, getTsi, "C:\\TSI_M1.csv");
    }
    public void Fractals() {
      DateTime timer = DateTime.Now;
      Rate[] rates = o2g.GetBars(o2g.Pair, 1, DateTime.Now.AddHours(-8)).ToArray();
      Debug.WriteLine("Get Ticks:" + (DateTime.Now - timer).TotalSeconds);
      Func<Rate , double> getFractal = t => t.FractalBuy > 0 ? -1 : t.FractalSell > 0 ? 1 : 0;
      timer = DateTime.Now;
      rates.FillFractal((r,d)=>r.Fractal = (FractalType)d);
      Debug.WriteLine("Get Fractal:" + (DateTime.Now - timer).TotalSeconds);
      SaveToFile(rates, getFractal, "C:\\Fractal.csv");
    }
    public void StdDev() {
      DateTime timer = DateTime.Now;
      Func<Rate, double?> getRsi = r => r.PriceRsi;
      Action<Rate, double?> setRsi = (r, d) => r.PriceRsi = d;
      Rate[] rates = o2g.GetTicks(o2g.Pair, 10000).ToArray().GroupTicksToRates().OrderBars().ToArray();
      Debug.WriteLine("Get Ticks:" + (DateTime.Now-timer).TotalSeconds);

      timer = DateTime.Now;
      rates.FillRsi(14, r => r.PriceClose);
      SaveToFile(rates, getRsi, "C:\\RSI.csv");
      Debug.WriteLine("StdDev:" + rates.StdDev(getRsi));
      Debug.WriteLine("Get Rsi:" + (DateTime.Now - timer).TotalSeconds);
    }
    public void GetRSI() {
      var rates = o2g.GetTicks(o2g.Pair, 10000).ToArray();
      var t = DateTime.Now;
      var dd = rates.FillRsi(14, r => r.PriceClose);// Indicators.RSI(rates, r => r.AskClose, 14);
      System.Diagnostics.Debug.WriteLine((DateTime.Now - t).TotalSeconds + " ms");
      SaveToFile(rates, r=>r.PriceRsi, "C:\\RSI.csv");
    }
    void SaveToFile<T, D>(IEnumerable<T> rates, Func<T, D> price, string fileName) where T : Rate {
      StringBuilder sb = new StringBuilder();
      sb.Append("Time,Price,Indicator" + Environment.NewLine);
      rates.ToList().ForEach(r => sb.Append(r.StartDate.ToShortDateString() + " " + r.StartDate.ToLongTimeString() + "," + r.PriceClose + "," + price(r) + Environment.NewLine));
      using (var f = System.IO.File.CreateText(fileName)) {
        f.Write(sb.ToString());
      }
    }
    public void GetRLW() {
      var rates = o2g.GetTicks(o2g.Pair, 15000).OrderBars().ToArray();
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
    void FillRLW_RSI(Tick[] ticks, int period, DateTime dateStart, DateTime dateEnd
      , Action<Rate, double> priceRsi, Action<Rate, double> priceRsiCR, Action<Rate, double> priceRlw) {
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
      var ticks = o2g.GetTicks(o2g.Pair, 5000);
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
