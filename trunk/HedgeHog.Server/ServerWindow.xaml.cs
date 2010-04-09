using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Order2GoAddIn;
using O2G = Order2GoAddIn;
using FXW = Order2GoAddIn.FXCoreWrapper;
using HedgeHog;
using HedgeHog.Bars;

namespace HedgeHog {
  public sealed partial class ServerWindow : Window, IServer, INotifyPropertyChanged, IDisposable {
    #region Log
    object Log {
      set {
        if ((value + "").Length == 0) return;
        var exc = value as Exception;
        var message = exc == null ? value + "" : exc.Message;
        txtLog.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(delegate() {
          var time = DateTime.Now.ToString("[dd HH:mm:ss] ");
          txtLog.Text += time + message + Environment.NewLine;
          txtLog.ScrollToEnd();
          var text = time + message + Environment.NewLine + (exc == null ? "" : exc.StackTrace + Environment.NewLine);
          while (exc != null && (exc = exc.InnerException) != null)
            text += "**************** Inner ***************" + Environment.NewLine + exc.Message + Environment.NewLine + exc.StackTrace + Environment.NewLine;
          System.IO.File.AppendAllText(logFileName, text);
        })
        );
      }
    }
    object VLog { set { if (ui.verboseLogging)Log = value; } }
    #endregion


    #region Settings

    public bool TestMode { get { return ui.timeFrameTimeStart != DateTime.FromOADate(0); } }


    DateTime ticksStartDate { get { return ServerTime.AddMinutes(-ui.timeFrameMinutesMaximum); } }


    int tcpPort;
    public int TcpPort {
      get { return tcpPort; }
      set { tcpPort = value; RaisePropertyChanged(() => TcpPort, () => TitleAndPort); }
    }

    public string TitleAndPort { get { return "HedgeHog Server : " + tcpPort + " -> " + cmbPair.Text; } }


    static bool drawWaves { get { return false; } }

    private UI ui = new UI();

    #endregion

    #region Properties
    string logFileName = "Log.txt";
    public static string CurrentDirectory { get { return AppDomain.CurrentDomain.BaseDirectory; } }
    static Order2GoAddIn.CoreFX coreFX { get { return (Application.Current as Server.App).CoreFX; } }
    Order2GoAddIn.FXCoreWrapper fw;
    List<Rate> _ticks = new List<Rate>();
    List<Rate> Ticks {
      get { return doTicks || lastBar.Count == 0 ? _ticks : _ticks.Concat(new[] { lastBar }).ToList(); }
      set { _ticks = value; }
    }
    List<Volt> Voltages = new List<Volt>();
    List<Rate> RatesHigh = new List<Rate>();
    Corridors CorridorsWindow_EURJPY;
    ThreadScheduler VoltageScheduler;
    ThreadScheduler TicksScheduler;
    ThreadScheduler SaveToFileScheduler;
    ThreadScheduler VolatilityScheduler;
    ThreadScheduler DecisionScheduler;
    Scheduler CorridorsScheduler;
    ThreadScheduler MinutesBackScheduler;
    Func<Rate, double> spreadLambda = r => r.Spread;
    Rate tickHigh, tickLow;
    #endregion

    #region Statistics
    public O2G.Price Price { get; set; }
    private DateTime _serverTimeCached;
    public DateTime ServerTime {
      get { return fw == null ? DateTime.Now : _serverTimeCached = fw.ServerTime; }
    }
    public double Ask { get { return Price != null ? Price.Ask : 0; } }
    public double Bid { get { return Price != null ? Price.Bid : 0; } }
    public int AskChangeDirection { get { return Price != null ? Price.AskChangeDirection : 0; } }
    public int BidChangeDirection { get { return Price != null ? Price.BidChangeDirection : 0; } }

    public double PeakPriceHigh { get; set; }
    private double _ppha;
    public double PeakPriceHighAverage {
      get { return _ppha; }
      set {
        _ppha = value;
        RaisePropertyChanged(() => PeakPriceHighAverage);
      }
    }
    public double ValleyPriceLow { get; set; }
    private double _vpla;
    public double ValleyPriceLowAverage {
      get { return _vpla; }
      set {
        _vpla = value;
        RaisePropertyChanged();
      }
    }

    VoltForGrid PeakVolt = new VoltForGrid(), ValleyVolt = new VoltForGrid();
    public DateTime PeakStartDate { get { return PeakVolt.StartDate; } }
    public DateTime ValleyStartDate { get { return ValleyVolt.StartDate; } }
    public double VoltageSpread {
      get { return VoltPriceHigh - VoltPriceLow; }
    }
    public double VoltageSpreadInPips {
      get { return fw.InPips(VoltageSpread); }
    }
    public double CorridorSpread(bool doTrend) {
      if (doTrend)
        return Fractals.Length < 2 ? 0 : Fractals.Skip(1).Select((f, i) => Math.Abs(f.FractalPrice.Value - Fractals[i].FractalPrice.Value))
          .OrderByDescending(d => d).Take(4).Average();
      return doTrend ? VolatilityUpPeak + VolatilityDownPeak : Math.Max(VoltageSpread, PeakPriceHigh - ValleyPriceLow);
    }
    public double GetCorridorSpreadInPips(bool doTrend) { return fw.IsLoggedIn ? fw.InPips(CorridorSpread(doTrend), 1) : 0; }
    public double CorridorSpreadInPips { get { return GetCorridorSpreadInPips(true); } }
    /// <summary>
    /// Must be synced with TradeStatistics.CorridorSpreadAvg
    /// </summary>
    public int TimeframeInMinutes { get { return Timeframe == DateTime.MinValue ? 0 : (ServerTime - Timeframe).TotalMinutes.ToInt(); } }
    DateTime _timeframe = DateTime.MinValue;
    public DateTime Timeframe {
      get { return _timeframe; }
      set {

        if (_timeframe == value) return;
        _timeframe = value;
        RaisePropertyChanged(() => TimeframeInMinutes);
        RaisePropertyChanged(() => PeakPriceHighAverage);
      }
    }
    private bool? _IsBuyMode;
    public bool? IsBuyMode {
      get { return _IsBuyMode; }
      set {
        if (IsBuyMode == value) return;
        _IsBuyMode = value;
        RaisePropertyChangedCore();
        if (IsBuyMode == null) FractalBuy = FractalSell = new Rate();
      }
    }
    private double? _CorridorHeightMinutesBySchedule;
    public int CorridorHeightMinutesBySchedule {
      get {
        return _CorridorHeightMinutesBySchedule.HasValue
          ? _CorridorHeightMinutesBySchedule.Value.Ceiling() : ui.corridorHeightMinutesBySchedule;
        return Math.Floor(TimeframeByTicksMin / (double)ui.corridorHeightMinutes).ToInt();
      }
      set {
        _CorridorHeightMinutesBySchedule = value;
      }
    }

    private Rate _FractalBuy;
    public Rate FractalBuy {
      get { return _FractalBuy; }
      set { if (_FractalBuy == value)return; _FractalBuy = value; RaisePropertyChangedCore(); }
    }
    private bool _FractalBuyColor;
    public bool FractalBuyColor {
      get { return _FractalBuyColor; }
      set { _FractalBuyColor = value; RaisePropertyChangedCore(); }
    }

    private Rate _FractalSell;
    public Rate FractalSell {
      get { return _FractalSell; }
      set { if (_FractalSell == value)return; _FractalSell = value; RaisePropertyChangedCore(); }
    }

    private bool _FractalSellColor;
    public bool FractalSellColor {
      get { return _FractalSellColor; }
      set { _FractalSellColor = value; RaisePropertyChangedCore(); }
    }

    private bool _fractalWaveColor = true;
    public bool FractalWaveColor {
      get { return _fractalWaveColor; }
      set { if (_fractalWaveColor != value) { _fractalWaveColor = value; RaisePropertyChangedCore(); } }
    }

    public int TimeframeInMinutesAlt { get { return TimeframeAlt == DateTime.MinValue ? 0 : (ServerTime - TimeframeAlt).TotalMinutes.ToInt(); } }
    DateTime _timeframeAlt = DateTime.MinValue;
    public DateTime TimeframeAlt {
      get { return _timeframeAlt; }
      set {
        _timeframeAlt = value;
        RaisePropertyChanged(() => TimeframeInMinutesAlt);
      }
    }
    public int TimeframeByTicksMin {
      get {
        return Ticks.Count == 0 ? 0
        : doTicks ? (ServerTime - Ticks.OrderBarsDescending().Take(ui.ticksBack).Last().StartDate).TotalMinutes.ToInt()
        : ui.timeFrameMinutesMinimum;
      }
    }
    private int _minutesBackSampleCount;

    public int MinutesBackSampleCount {
      get { return _minutesBackSampleCount; }
      set {
        _minutesBackSampleCount = value;
        RaisePropertyChangedCore();
      }
    }

    private double _minutesBackSpeed;

    public double MinutesBackSpeed {
      get { return _minutesBackSpeed; }
      set {
        _minutesBackSpeed = value;
        RaisePropertyChangedCore();
      }
    }

    double _corridorSpreadMinimum;
    public double CorridorSpreadMinimum {
      get { return _corridorSpreadMinimum; }
      set {
        _corridorSpreadMinimum = value;
        RaisePropertyChanged(() => CorridorSpreadMinimum, () => CorridorSpreadMinimumInPips);
      }
    }
    public double CorridorSpreadMinimumInPips { get { return fw.InPips(CorridorSpreadMinimum, 1); } }
    #region Bar Spreads
    double _spreadAverage;
    public double SpreadAverage {
      get { return _spreadAverage; }
      set { _spreadAverage = value; RaisePropertyChanged(() => SpreadAverage, () => SpreadAverageInPips); }
    }
    public double SpreadAverageInPips { get { return fw.InPips(SpreadAverage, 1); } }

    double _spreadAverageShort;
    public double SpreadAverageShort {
      get { return _spreadAverageShort; }
      set { _spreadAverageShort = value; RaisePropertyChanged(() => SpreadAverageShort, () => SpreadAverageShortInPips); }
    }
    public double SpreadAverageShortInPips { get { return fw.InPips(SpreadAverageShort, 1); } }

    double _spreadAverage5Min;
    public double SpreadAverage5Min {
      get { return _spreadAverage5Min; }
      set { _spreadAverage5Min = value; RaisePropertyChanged(() => SpreadAverage5Min, () => SpreadAverage5MinInPips); }
    }
    public double SpreadAverage5MinInPips { get { return fw.InPips(SpreadAverage5Min, 1); } }

    double _spreadAverage10Min;
    public double SpreadAverage10Min {
      get { return _spreadAverage10Min; }
      set { _spreadAverage10Min = value; RaisePropertyChanged(() => SpreadAverage10Min, () => SpreadAverage10MinInPips); }
    }
    public double SpreadAverage10MinInPips { get { return fw.InPips(SpreadAverage10Min, 1); } }

    double _spreadAverage15Min;
    public double SpreadAverage15Min {
      get { return _spreadAverage15Min; }
      set { _spreadAverage15Min = value; RaisePropertyChanged(() => SpreadAverage15Min, () => SpreadAverage15MinInPips); }
    }
    public double SpreadAverage15MinInPips { get { return fw.InPips(SpreadAverage15Min, 1); } }

    int _ticksPerMinuteAverage;
    public int TicksPerMinuteAverage {
      get { return _ticksPerMinuteAverage; }
      set { _ticksPerMinuteAverage = value; RaisePropertyChanged(); }
    }
    int _ticksPerMinuteAverageLong;
    public int TicksPerMinuteAverageLong {
      get { return _ticksPerMinuteAverageLong; }
      set { _ticksPerMinuteAverageLong = value; RaisePropertyChanged(); }
    }
    #endregion

    public double VoltPriceHigh {
      get { return Math.Round(PeakVolt.AverageBid, fw.Digits); }
    }
    public double VoltPriceLow {
      get { return Math.Round(ValleyVolt.AverageAsk, fw.Digits); }
    }

    public double SpreadByBarPeriod(int period, bool shortOnly) {
      var spreadShort = (_ticksInTimeFrame.Length > 0 ? _ticksInTimeFrame : TicksInTimeFrame).GetMinuteTicks(period).Average(spreadLambda);
      if (shortOnly || RatesHigh.Count == 0) return spreadShort;
      var spreadLong = fw.GetMinuteBars(RatesHigh.ToArray(), period).Average(spreadLambda);
      return Math.Max(spreadShort, spreadLong);
    }
    public double Angle {
      get { return Math.Atan(A) * (180 / Math.PI); }
    }
    public double AngleRounded { get { return Math.Round(Angle / fw.PointSize, 2); } }
    double A;
    double[] _regressionCoeffs = new double[] { };

    double[] RegressionCoefficients {
      get { return _regressionCoeffs; }
      set {
        _regressionCoeffs = value;
        A = value[1];
        RaisePropertyChanged(() => AngleRounded);
      }
    }
    Signaler.DataPoint[] waves = new Signaler.DataPoint[] { };
    public double WavesRatio {
      get {
        return _ticksInTimeFrame.Length == 0 ? 0 : (VolatilityUpPeak + VolatilityDownPeak) / (_ticksInTimeFrame.Max(t => t.PriceClose) - _ticksInTimeFrame.Min(t => t.PriceClose));
      }
    }
    double _volatilityUp;
    public double VolatilityUp {
      get { return _volatilityUp; }
      set {
        _volatilityUp = value;
        RaisePropertyChanged(() => VolatilityUp, () => VolatilityUpInPips);
      }
    }
    public double VolatilityUpInPips { get { return fw.InPips(VolatilityUp); } }

    double _volatilityUpPeak;
    public double VolatilityUpPeak {
      get { return _volatilityUpPeak; }
      set {
        _volatilityUpPeak = value;
        RaisePropertyChanged(() => VolatilityUpPeak, () => VolatilityUpPeakInPips);
      }
    }
    public double VolatilityUpPeakInPips { get { return fw.InPips(VolatilityUpPeak); } }


    double _volatilityDown;
    public double VolatilityDown {
      get { return _volatilityDown; }
      set {
        _volatilityDown = value;
        RaisePropertyChanged(() => VolatilityDown, () => VolatilityDownInPips);
      }
    }
    public double VolatilityDownInPips { get { return fw.InPips(VolatilityDown); } }

    public double VolatilityMaxInPips { get { return Math.Max(VolatilityUpInPips, VolatilityDownInPips); } }
    public double VolatilityAvgInPips { get { return (VolatilityUpInPips + VolatilityDownInPips) / 2; } }

    double _volatilityDownPeak;
    public double VolatilityDownPeak {
      get { return _volatilityDownPeak; }
      set {
        _volatilityDownPeak = value;
        RaisePropertyChanged(() => VolatilityDownPeak, () => VolatilityDownPeakInPips);
      }
    }
    public double VolatilityDownPeakInPips { get { return fw.InPips(VolatilityDownPeak); } }

    double priceHeight;
    public double PriceHeight {
      get { return priceHeight; }
      set {
        priceHeight = value;
        RaisePropertyChanged(() => PriceHeight, () => PriceHeightInPips);
      }
    }
    public double PriceHeightInPips { get { return fw.InPips(PriceHeight); } }
    Func<Rate, double> heightLambda = new Func<Rate, double>(t => t.PriceAvg - t.PriceAvg1);



    public bool? IsCorridorVolatilityByMin {
      get { return (bool?)GetValue(IsCorridorVolatilityByMinProperty); }
      set { SetValue(IsCorridorVolatilityByMinProperty, value); }
    }
    public static readonly DependencyProperty IsCorridorVolatilityByMinProperty =
        DependencyProperty.Register("IsCorridorVolatilityByMin", typeof(bool?), typeof(ServerWindow), new UIPropertyMetadata(null,
          (d, e) => {
          }
          ));

    int _rsiBuy;
    public int RsiBuy {
      get { return _rsiBuy; }
      set { _rsiBuy = value; RaisePropertyChangedCore(); }
    }
    int _rsiSell;
    public int RsiSell {
      get { return _rsiSell; }
      set { _rsiSell = value; RaisePropertyChangedCore(); }
    }
    double _rsiStdDev;
    public double RsiStdDev {
      get { return _rsiStdDev; }
      set { _rsiStdDev = value; RaisePropertyChangedCore(); }
    }
    double _rsiAverage;
    public double RsiAverage {
      get { return _rsiAverage; }
      set { _rsiAverage = value; RaisePropertyChangedCore(); }
    }
    double _rsiOffset;
    public double RsiOffset {
      get { return _rsiOffset; }
      set { _rsiOffset = value; RaisePropertyChangedCore(); }
    }
    double _rsiMaximum;

    /*
    public double RsiMaximum {
      get { return _rsiMaximum; }
      set { _rsiMaximum = value; RaisePropertyChangedCore(); }
    }
    double _rsiMinimum;

    public double RsiMinimum {
      get { return _rsiMinimum; }
      set { _rsiMinimum = value; RaisePropertyChangedCore(); }
    }
    */
    #endregion

    #region Ctor
    public ServerWindow() {
      fw = new Order2GoAddIn.FXCoreWrapper(coreFX);
      InitializeComponent();
      System.IO.File.Delete(logFileName);
      Closing += new System.ComponentModel.CancelEventHandler(ServerWindow_Closing);
      coreFX.LoggedInEvent += new EventHandler<EventArgs>(coreFX_LoggedInEvent);
      coreFX.LoginError += new Order2GoAddIn.CoreFX.LoginErrorHandler(coreFX_LoginError);
      fw.Pair = cmbPair.Text;
      fw.OrderAdded += new Order2GoAddIn.FXCoreWrapper.OrderAddedEventHandler(fxCoreWrapper_EURJPY_OrderAdded);
      fw.RowRemoving += new Order2GoAddIn.FXCoreWrapper.RowRemovingdEventHandler(fxCoreWrapper_EURJPY_RowRemoving);
      fw.PriceChanged += new Order2GoAddIn.FXCoreWrapper.PriceChangedEventHandler(fxCoreWrapper_EURJPY_PriceChanged);
      CorridorsWindow_EURJPY = new Corridors(this.Name);
      CorridorsScheduler = new Scheduler(CorridorsWindow_EURJPY.Dispatcher, (s, e) => Log = e.Exception);
      VoltageScheduler = new ThreadScheduler(TimeSpan.FromMilliseconds(1), (s, e) => Log = e.Exception);
      TicksScheduler = new ThreadScheduler((s, e) => Log = e.Exception);
      TicksScheduler.Finished += (sender, e) => {
        if (getTicksCommand != null) {
          TicksScheduler.Command = getTicksCommand;
          getTicksCommand = null;
        }
      };
      MinutesBackScheduler = new ThreadScheduler((s, e) => Log = e.Exception);
      DecisionScheduler = new ThreadScheduler((s, e) => Log = e.Exception);
    }
    #endregion

    #region GetTicks
    readonly DateTime fxDateNow = DateTime.FromOADate(0);
    ThreadScheduler.CommandDelegate getTicksCommand;
    void GetTicksAsync() {
      if (!TestMode) {
        GetTicksAsync(fxDateNow, fxDateNow);
      } else
        GetTicksAsync(timeFrameDateStart, ui.timeFrameTimeStart);
    }
    void GetTicksAsync(DateTime StartDate, DateTime EndDate) {
      if (TicksScheduler == null || !fw.IsLoggedIn) return;
      //if (VolatilityScheduler.IsRunning)
      //  VolatilityScheduler.WaitHandler.WaitOne(1000);
      ThreadScheduler.CommandDelegate c = () => GetTicks(StartDate, EndDate);
      if (TicksScheduler.IsRunning || VolatilityScheduler.IsRunning) getTicksCommand = c;
      else TicksScheduler.Command = c;
    }
    bool doTicks { get { return ui.ticksBack > 60; } }
    int ratePeriod { get { return doTicks ? 0 : ui.ticksBack; } }
    bool fractalChanged;
    void GetTicks(DateTime StartDate, DateTime EndDate) {
      try {
        if (StartDate == fxDateNow) StartDate = ticksStartDate;
        StartDate = Lib.Min(timeFrameDateStart, StartDate);
        if (ratePeriod == 0 && _ticks.Count == 0 || fractalChanged) {
          fractalChanged = false;
          Ticks = fw.GetTicks(ui.ticksBack).OfType<Rate>().OrderBars().ToList();
          Ticks.AddUp(fw.GetTicks(300));
          TicksPerMinuteAverageLong = _ticks.TradesPerMinute().ToInt();
        }
        var ticks = _ticks.TakeWhile(b => b.IsHistory).ToArray();
        if ((_ticks.Last().StartDate - ticks.Last().StartDate).Duration().TotalSeconds > 30) {
          //fw.GetBars(ratePeriod, StartDate, EndDate, ref ticks);
          Ticks = ticks.AddUp(fw.GetTicks(300)).ToList();
          Ticks.FillMass();
          TicksPerMinuteAverageLong = _ticks.TradesPerMinute().ToInt();
        }
        //TicksPerMinute(Ticks);
        RaisePropertyChanged(() => TimeframeByTicksMin);
        ShowTicks();
        _ticks.ToArray().FillPower(TimeSpan.FromMinutes(1));
        RunMinutesBack(null);
      } catch (Exception exc) {
        Log = exc;
      }
      //Select((b, i) => new Tick() {
      //  Ask = b.Ask, Bid = b.Bid, StartDate = b.StartDate, Row = i + 1, IsHistory = b.IsHistory
      //}).ToList();
    }
    public void TicksPerMinute(List<Rate> ticks) {
      var dateFrom = DateTime.Parse("3/31/2010 12:40:12");
      var dateTo = DateTime.Parse("3/31/2010 12:49:25");
      var tl = ticks.Where(dateFrom,dateTo).ToArray();
      Debug.WriteLine("TicksPerMinute:{0: HH:mm:ss} -{1: HH:mm:ss}={2:n0}/{3}", dateFrom, dateTo,
        tl.TradesPerMinute(), tl.SumMass());
      var rate = new Rate();
      ticks.FillPower(rate);
    }

    Func<Rate, double> getPrice = r => r.PriceClose;
    Func<Rate, double?> getRsi = r => r.PriceRsi;
    Action<Rate, double?> setRsi = (r, d) => r.PriceRsi = d;
    #endregion

    #region TimeFrame
    DateTime dateMin {
      get {
        if (ui.timeFrameTimeStart == DateTime.FromOADate(0))
          return ServerTime.AddMinutes(-ui.timeFrameMinutesMinimum);
        return ui.timeFrameTimeStart.AddMinutes(-ui.timeFrameMinutesMaximum);
      }
    }
    DateTime timeFrameDateStart {
      get {
        var tick = Ticks.Reverse<Rate>().Take(ui.ticksBack).LastOrDefault();
        return new[] { dateMin, tick == null ? dateMin : tick.StartDate, ServerTime.AddMinutes(-ui.timeFrameMinutesMaximum) }.Min();
      }
    }
    private static void SaveRateToFile(List<Rate> ticks, Func<Rate, double> value1, Func<Rate, double> value2, Func<Rate, double> value3) {
      try {
        var s = new StringBuilder();
        ticks.OrderBars().ToList().ForEach(v => s.Append(v.StartDate + "," + value1(v) + "," + value2(v) + "," + value3(v) + Environment.NewLine));
        System.IO.File.WriteAllText("C:\\Volts.csv", s.ToString());
      } catch { }
    }
    struct WaveInfo {
      public DateTime StartDate;
      public double WaveRatio;
      public double TradePosition;
      public double UpDownRatio;
      public double[] Coeffs;
      public TimeSpan Interval;
      public double vu;
      public double vd;
      public WaveInfo(DateTime startDate, double waveRatio, double tradePosition, double upDownRatio, double vu, double vd, double[] coeffs, TimeSpan interval) {
        StartDate = startDate;
        WaveRatio = waveRatio;
        TradePosition = tradePosition;
        UpDownRatio = upDownRatio;
        Coeffs = coeffs;
        Interval = interval;
        this.vu = vu;
        this.vd = vd;
      }
    }
    double priceHeightMaxOld;
    double priceHeightMinOld;
    struct DateToScan {
      public DateTime Date;
      public TimeSpan Interval;
      public DateToScan(DateTime date, TimeSpan interval) {
        Date = date;
        Interval = interval;
      }
    }
    double priceHeightMax = 0;
    double priceHeightMin = 0;
    Func<Rate, double> readFrom = r => r.PriceAvg;
    bool IsTsiWaiveHigh(Rate rate, double average, double stDev) { return rate.PriceTsi > average + stDev; }
    bool IsTsiWaiveLow(Rate rate, double average, double stDev) { return rate.PriceTsi < average - stDev; }
    bool IsTsiWaive(Rate rate, double average, double stDev) { return IsTsiWaiveHigh(rate, average, stDev) || IsTsiWaiveLow(rate, average, stDev); }
    static public class Extension {
    }

    Rate GetFractalWave(Rate[] ratesFractal, int waveCountMinimum) {
      if (ratesFractal.Count() < 5) return null;
      SetTicksPrice(ratesFractal, 1, r => r.PriceAvg, (r, d) => r.PriceAvg1 = d);
      var stDev = ratesFractal.StdDev(r => r.PriceAvg - r.PriceAvg1);
      ratesFractal = ratesFractal.HasFractal(r => Math.Abs(r.PriceAvg - r.PriceAvg1) > stDev).ToArray();
      var rateFractal = ratesFractal.LastOrDefault(r => r.Fractal > 0);
      for (int waveCount = 0; rateFractal != null && ++waveCount < waveCountMinimum; )
        rateFractal = ratesFractal.Where(r => r.StartDate < rateFractal.StartDate)
          .LastOrDefault(r => rateFractal.FractalSell != 0 ? r.FractalBuy != 0 : r.FractalSell != 0);
      return rateFractal;
    }

    Rate GetTsiWave(Rate[] ratesTsi, int waveCountMinimum) {
      if (ratesTsi.Count() == 0) return null;
      var tsiStDev = ratesTsi.StdDev(r => r.PriceTsi);
      var tsiAnerage = ratesTsi.Average(r => r.PriceTsi).Value;
      var tsiWave = ratesTsi.Where(r => IsTsiWaive(r, tsiAnerage, tsiStDev)).OrderBy(r => r.StartDate).LastOrDefault();
      for (int waveCount = 0; tsiWave != null && ++waveCount < waveCountMinimum; )
        tsiWave = ratesTsi.Where(r => r.StartDate < tsiWave.StartDate)
          .Where(r => tsiWave.PriceTsi > 0 ? IsTsiWaiveLow(r, tsiAnerage, tsiStDev) : IsTsiWaiveHigh(r, tsiAnerage, tsiStDev))
          .OrderBy(r => r.StartDate).LastOrDefault();
      return tsiWave;
    }
    IEnumerable<Rate> TicksForTimeFrame {
      get {
        return
          (doTicks ? ui.groupTicks ? Ticks.GroupTicksToRates() : Ticks : Ticks);
      }
    }
    void CalculateTimeFrame(object o) {
      var paramArray = ((object[])o);
      var tickLocal = (Rate)paramArray[0];
      var eventLocal = (ManualResetEvent)paramArray[1];
      var ticksCopy = (List<Rate>)paramArray[2];
      var writeTo = (Action<Rate, double>)paramArray[3];
      var tickLast = (Rate)paramArray[4];
      var wi = (List<WaveInfo>)paramArray[5];
      try {
        while (true) {
          var coeffs = SetTicksPrice(ticksCopy, 1, readFrom, writeTo);
          var tickHigh = ticksCopy.OrderByDescending(t => t.PriceHigh).First();
          var tickLow = ticksCopy.OrderBy(t => t.PriceLow).First();
          var vu = GetVolatilityUp(ticksCopy.ToList(), ui.volatilityWieght);
          var vd = GetVolatilityDown(ticksCopy.ToList(), ui.volatilityWieght);
          var vh = vu + vd;
          //if (vh < CorridorSpreadMinimum) break;
          var priceHeightUp = priceHeightMax - tickLast.PriceAvg4;
          var priceHeightDown = tickLast.PriceAvg4 - priceHeightMin;
          ////corridorHeightMin = fooCorrHeightMinimum(tickHigh.PriceHigh, tickLow.PriceLow);
          ////if (Math.Min(vd, vu) < corridorHeightMin) break;
          var upDownRatio = Lib.FibRatio(vu, vd);
          var posBuy = /*Math.Abs(*/priceHeightDown / vd - 1/*)*/;
          var posSell = /*Math.Abs(*/priceHeightUp / vu - 1/*)*/;
          var poss = new[] { posBuy, posSell };
          //var posTrade = vd == vu || !ui.corridorByMinimumVolatility.HasValue ? Math.Min(posSell, posBuy) : fooPosTradeCondition(vd, vu) ? posBuy : posSell;
          var posTrade = Lib.FibRatio(posBuy, posSell);
          var stDev = ticksCopy.StdDev(t => readFrom(t) - t.PriceAvg4);
          var waveRatio = stDev / (vd + vu);// Math.Min(vd, vh) / (tickHigh.PriceClose - tickLow.PriceClose);
          wi.Add(new WaveInfo(tickLocal.StartDate, Math.Round(waveRatio, 3), Math.Round(posTrade, 2), upDownRatio, /*Math.Abs(vu / vd - vd / vu)*/ vu, vd, coeffs, TimeSpan.Zero/*interval*/));
          break;
        }
      } catch (Exception exc) {
        Log = exc;
      }
      eventLocal.Set();
    }
    Rate[] Fractals = new Rate[] { };
    Rate[] Fractals1 = new Rate[] { };

    static string Format1(double value,double minValue,string formatMin,string formatOther){
      return value.ToString(Math.Abs(value) < minValue ? formatMin : formatOther);
    }
    public string FractalWavesText {
      get {
        try {
          double tpm;
          return string.Join(Environment.NewLine, Fractals1.
            Select((f, i) => f.StartDate.ToString("HH:mm:ss") + " " + (f.Fractal == FractalType.Buy ? "B" : f.Fractal == FractalType.Sell ? "S" : "N")
              + (i < Fractals1.Length - 1
              ? "|" + fw.InPips(f.Ph.Height.Abs(), 0).ToInt().ToString("00")
              + "|" + (tpm = _ticks.TradesPerMinute(f, Fractals1[i + 1])).ToString("00")
              + "|" + fw.InPips(1, f.Ph.Mass.Value, 0).ToString("000")
              + "|" + fw.InPips(1, f.Ph.MassPerTradesPerMinute.Value).ToString("n2")
              //+ "|" + Format1(fw.InPips(2, f.Ph.Power.Value, 1), 10, " 0.0;-0.0", " 00;-00")
              : ""
              )).ToArray());
        } catch (Exception exc) { Log = exc; return "Error"; }
      }
    }
    public TimeSpan OverlapAverage { get; set; }
    public TimeSpan OverlapStDev { get; set; }
    public TimeSpan OverlapAverageShort { get; set; }
    public TimeSpan OverlapLast { get; set; }
    public double OverlapLastPerc { get; set; }
    public TimeSpan OverlapFractal { get; set; }
    public double OverlapAverageShortPower { get; set; }
    public double OverlapAveragePower { get; set; }
    public double OverlapLastPower { get; set; }
    double? legAverageBottom;
    private Rate FractalLastPowerMax = new Rate();
    double? fractalPowerAverage;
    private double? waveHeight;
    public double WaveHeightInPips { get { return fw.InPips(waveHeight.GetValueOrDefault(), 1); } }
    public bool TradeByShortOverlapOk { get { return OverlapLast.TotalSeconds > OverlapAverageShort.TotalSeconds.ToInt(); } }
    private bool tradeByOverlap { get { return false && OverlapLast.TotalMinutes > Math.Ceiling(OverlapAverageShort.TotalMinutes); } }
    public bool TradeByOverlapBuy { get { return !isInSell && tradeByOverlap; } }
    public bool TradeByOverlapSell { get { return isInSell && tradeByOverlap; } }
    public string FractalStats { get; set; }
    public bool IsPowerDown { get; set; }
    double legUpAverageInPips;
    double legDownAverageInPips;
    double baseHeight;
    double baseHeightInPips { get { return fw.InPips(baseHeight); } }
    bool isInSell { get { return Fractals[0].HasFractalBuy; } }
    double posBS;
    bool isLegOk, isMassOk, isPosBSOk;
    TimeSpan wavePeriodShort;
    Signaler.WaveStats WaveStats = new Signaler.WaveStats();
    double PowerInPips(double power, int roundTo) { return fw.InPips(2, power, roundTo); }
    private static void DebugWriteLineStopWatch(string line, Stopwatch sw) {
      System.Diagnostics.Debug.WriteLine(line + sw.ElapsedMilliseconds + "ms.");
      sw.Reset(); sw.Start();
    }
    public delegate double FuncBase(Func<BarBase, double?> value);
    public delegate double FuncBase1(IEnumerable<BarBase> bars, Func<BarBase, double?> value);
    FuncBase1 Average4 = (fractals,f) => {
      var fs = fractals.Take(3).TakeWhile(t => f(t).HasValue).Select(f);
      return new[] { fs.First(), fs.Last() }.Average().Value;
    };
    private void GetPositionByFractals() {
      FuncBase bsBaseFoo1 = (f) => Fractals.Take(4).OrderBy(f).Skip(1).OrderByDescending(f).Skip(1).Select(f).Average().Value;
      FuncBase bsBaseFoo2 = (f) => Fractals.Take(3).Average(f).Value;
      FuncBase bsBaseFoo3 = (f) => Fractals.Take(3).Select(f).OrderByDescending(m => m).Take(2).Average().Value;
      FuncBase bsBaseFoo4 = (f) => Average4(Fractals.Skip(1), f);
        var bsBaseFoo = bsBaseFoo4;
      try {
        var ret = new List<string>();
        var sw = Stopwatch.StartNew();
        #region TickByMinute
        var ticksByMinute = Ticks.GetMinuteTicks(1).OrderBarsDescending().ToArray();
        var dateLast = ServerTime.AddSeconds(-30);
        var ticksByMinute1 = Ticks.Where(t => t.StartDate < dateLast).GetMinuteTicks(1).OrderBarsDescending().ToArray();
        TicksPerMinuteAverage = (Fractals1.Length < 2 ? TicksPerMinuteAverageLong : _ticks.TradesPerMinute(Fractals1[0], Fractals1[1])).ToInt();
        var cmaPeriod = TicksPerMinuteAverage / 2.0;
        #endregion

        #region Overlaps
        ticksByMinute.FillOverlaps();
        ticksByMinute1.FillOverlaps();
        OverlapAverage = TimeSpan.FromSeconds(Lib.CMA(OverlapAverage.TotalSeconds, 0, cmaPeriod,
          Math.Min(ticksByMinute.Average(r => r.Overlap.TotalSeconds), ticksByMinute1.Average(r => r.Overlap.TotalSeconds)))
          );
        OverlapStDev = TimeSpan.FromSeconds(Lib.CMA(OverlapStDev.TotalSeconds, 0, cmaPeriod,
          Math.Min(ticksByMinute.StdDev(r => r.Overlap.TotalSeconds), ticksByMinute1.StdDev(r => r.Overlap.TotalSeconds)))
          );
        OverlapAverageShort = Fractals.Length == 0? OverlapAverage :
          TimeSpan.FromSeconds(Lib.CMA(OverlapAverageShort.TotalSeconds, 0, 0,
          ticksByMinute.Where(t => t.StartDate >= Fractals[0].StartDate).Average(r => r.Overlap.TotalSeconds))
        );

        {
          var ticksForOL = ticksByMinute.Skip(1).Take(OverlapAverageShort.TotalMinutes.ToInt() * 2 + 1).ToArray();
          var tickLast = ticksByMinute.First();
          var ol = ticksForOL
            .Where(t => t.HasOverlap(tickLast) != OverlapType.None).Concat(new[] { tickLast }).OrderBars().ToArray();
          OverlapLast = TimeSpan.FromSeconds(/*Lib.CMA(OverlapLast.TotalSeconds, 0, 0, */
            ol.Length == 0 ? 0 : (ol.Last().StartDate - ol.First().StartDate).TotalSeconds/*)*/);
        }
        if (OverlapLast.TotalSeconds == 0) return;
        #endregion

        _CorridorHeightMinutesBySchedule = Lib.CMA(_CorridorHeightMinutesBySchedule, cmaPeriod, (TimeSpan.FromSeconds((OverlapAverage.TotalSeconds * 2))).TotalMinutes);


        #region Fractals/fractals
        if ((DateTime.Now - WaveStats.Time).TotalSeconds > 10)
          WaveStats = _ticks.GetWaves(TicksPerMinuteAverageLong).GetWaveStats();
        waveHeight = Lib.CMA(waveHeight, cmaPeriod, 0// WaveStats.Average + WaveStats.StDev//Math.Max(WaveStats.Average + WaveStats.StDev, WaveStats.AverageN)
          //Math.Max(SpreadByBarPeriod(5, false), priceCurrent.Spread * 3)
          //SpreadByBarPeriod(1, true) * OverlapAverage.TotalMinutes
          //SpreadByBarPeriod(CorridorHeightMinutesBySchedule, false)
       );
        var fractalCountMaximun = 10;
        var fractalTicks = _ticks.GroupTicksToRates().OrderBarsDescending().ToArray();
        Func<double?, Rate[]> findFractals = wave => fractalTicks.FindFractalTicks(wave.Value, TimeSpan.FromMinutes(CorridorHeightMinutesBySchedule), 1, fractalCountMaximun,b=>b.PriceHigh,b=>b.PriceLow).OrderBarsDescending().ToArray();
        Fractals = findFractals(waveHeight);

        _ticks.ToArray().FillPower(TimeSpan.FromMinutes(1));
        _ticks.ToArray().FillPower(Fractals);

        CorridorSpreadMinimum = Fractals.Skip(1).Select((f, i) => Math.Abs(f.PriceAvg - Fractals[i].PriceAvg)).Average();

        #region Normalize Fractal
        var legAllAverageStats = Fractals.Where(f => f.Ph.Height.HasValue).Select(f => f.Ph.Height.Abs().Value).ToArray().GetWaveStats();
        legAverageBottom = Lib.CMA(legAverageBottom, cmaPeriod, legAllAverageStats.AverageDown);
        if (ui.normalizeFractals) {
          Fractals = findFractals(legAverageBottom.Value);
          _ticks.ToArray().FillPower(Fractals);
          #region Old
          if (false) {
            if (Fractals[0].HasFractalBuy && priceCurrent.Bid < Fractals[0].BidLow
                || Fractals[0].HasFractalSell && priceCurrent.Ask > Fractals[0].AskLow) {
              Fractals = Fractals.Skip(1).ToArray();
              _ticks.ToArray().FillPower(Fractals);
            }
            var fractalsToLeave = new List<Rate>();
            Rate fractalPrev = null;
            foreach (var f in Fractals) {
              if (fractalPrev != null && fractalPrev.Ph.Height.Abs() < legAverageBottom) {
                fractalsToLeave.AddRange(new[] { f });
              }
              fractalPrev = f;
            }
            Fractals = Fractals.Except(fractalsToLeave).ToArray();
            Fractals = Fractals.FixFractals().ToArray();
          }
          #endregion
        }
        #endregion
        //Dimok: Make IsPowerOff
        //Dimok: Combine Mass0/Mass1 with Height0/Height1
        //OverlapAverageShort = TimeSpan.FromSeconds(ticksByMinute.Where(t => t.StartDate > Fractals[0].StartDate).Average(t => t.Overlap.TotalSeconds));
        #endregion

        Fractals1 = Fractals.Concat(new[] { _ticks.Last().Clone() as Tick }).OrderBarsDescending().ToArray();
        if (Fractals1.Length > 1)
          Fractals1[0].Fractal = Fractals1[1].HasFractalSell ? FractalType.Buy : FractalType.Sell;
        _ticks.ToArray().FillPower(Fractals1);
        var ticksAfterFractal = _ticks.Where(t=>t.StartDate>=Fractals1[1].StartDate).ToArray();
        Fractals1[0].Ph.Height = ticksAfterFractal.Max(t => t.PriceAvg) - ticksAfterFractal.Min(t => t.PriceAvg);


        if (Fractals.Length < 2) {
          ret.Add(Fractals.Length + " < 2");
          ret.Add("WS.Avg: " + fw.InPips(WaveStats.Average).ToString("n1"));
          ret.Add("WS.StD: " + fw.InPips(WaveStats.StDev).ToString("n1"));
          ret.Add("WS.Nvg: " + fw.InPips(WaveStats.AverageUp).ToString("n1"));
          FractalStats = string.Join(Environment.NewLine, ret.ToArray());
          RaisePropertyChanged(() => FractalStats);
          return;
        }

        #region Legs Up/Down
        //var legUps = Fractals1.Where(f => f.HasFractalSell && f.Ph.Height.HasValue).ToArray();
        //legUpAverageInPips = fw.InPips(legUps.Length == 0 ? 0 : legUps.Average(f => f.Ph.Height.Abs()), 0);
        legUpAverageInPips = fw.InPips(Average4(Fractals.SkipWhile(f => f.HasFractalBuy), f => f.Ph.Height.Abs()),1);
        //var waveUpsPeriod = legUps.Length == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(legUps.Average(f => (f.f.StartDate - Fractals[f.i + 1].StartDate).TotalSeconds));

        //var legDowns = Fractals1.Where(f => f.HasFractalBuy && f.Ph.Height.HasValue).ToArray();
        //legDownAverageInPips = fw.InPips(legDowns.Length == 0 ? 0 : legDowns.Average(f => f.Ph.Height.Abs()), 0);
        legDownAverageInPips = fw.InPips(Average4(Fractals.SkipWhile(f => f.HasFractalSell), f => f.Ph.Height.Abs()),1);
        //var waveDownsPeriod = legDowns.Length == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(legDowns.Average(f => (f.f.StartDate - Fractals[f.i + 1].StartDate).TotalSeconds));
        #endregion

        #region Fractals Short
        var wavePeriodShortTrade = OverlapAverageShort;// isInSell ? waveUpsPeriod : waveDownsPeriod;
        wavePeriodShort = wavePeriodShortTrade;// TimeSpan.FromSeconds(wavePeriodShortTrade.TotalSeconds * ui.fractalMinutes / 100.0);
        var fractalsShort = fractalTicks.FindFractals(0, wavePeriodShort, ui.fractalPadding, 2, b => b.PriceHigh, b => b.PriceLow);
        FractalSell = fractalsShort.SingleOrDefault(r => r.HasFractalSell);        //?? new Rate() { StartDate = ServerTime.Subtract(wavePeriodShort) };
        FractalBuy = fractalsShort.SingleOrDefault(r => r.HasFractalBuy);        //?? new Rate() { StartDate = ServerTime.Subtract(wavePeriodShort) }; 

        priceHeightMax = FractalSell == null ? 0 : Ticks.Where(t => t.StartDate >= FractalSell.StartDate).Max(readFrom); //tickHT.Max(readFrom);
        priceHeightMin = FractalBuy == null ? 0 : Ticks.Where(t => t.StartDate >= FractalBuy.StartDate).Min(readFrom);//tickHT.Min(readFrom);
        #endregion

        #region Legs
//        var fractalsWithHeight = fractals.Where(f => f.h > 0).OrderByDescending(f => f.h).ToArray();
//        var legAllAverage = fw.InPips(fractalsWithHeight.Skip(fractalsWithHeight.Length > 1 ? 1 : 0).Average(f => f.h), 1);
        var legAllAverage = legAllAverageStats.Average;
        var legTradeAverage = isInSell ? legUpAverageInPips : legDownAverageInPips;
        var fractalTradeLong = Fractals[0];
        var fractalTradeShort = fractalsShort.Count > 0 && fractalTradeLong.Fractal != fractalsShort[0].Fractal ? fractalsShort[0] : null;
        var tradeLeg = fractalTradeShort == null ? 0 : fw.InPips(
          isInSell ? fractalTradeShort.FractalPrice.Value - fractalTradeLong.FractalPrice.Value
          : fractalTradeLong.FractalPrice.Value - fractalTradeShort.FractalPrice.Value
          , 1);
        var tradePrice = priceCurrent.Average;
        var posBSByLeg = tradeLeg / legTradeAverage;
        var legPriceTicks = ticksByMinute.Where(t => t.StartDate > fractalTradeLong.StartDate);
        var legPrice = isInSell ?
          legPriceTicks.Max(t => t.PriceHigh) - fractalTradeLong.FractalPrice.Value
          : fractalTradeLong.FractalPrice.Value - legPriceTicks.Min(t => t.PriceLow);
        #endregion

        #region Positions %
        var posByPrice = fw.InPips(legPrice / legAllAverage, 1);
        var posByPriceLastLeg = Math.Abs(tradePrice - Fractals[0].FractalPrice.Value) / Fractals[0].Ph.Height.Abs();
        var masssRaio = (Fractals1[0].Ph.Mass / Fractals1[2].Ph.Mass).Value; //Math.Max(posBSByLeg, posByPrice);
        #endregion

        #region Fractal Last Power Average
        if (FractalLastPowerMax.Fractal != Fractals1[1].Fractal) {
          if( FractalLastPowerMax.Fractal != FractalType.None)
            this.fractalChanged = true;
          FractalLastPowerMax.Fractal = FractalType.None;
          FractalLastPowerMax.Fractal = Fractals1[1].Fractal;
          FractalLastPowerMax.Ph.Power = 0;
          fractalPowerAverage = null;
        }
        if (Math.Abs(FractalLastPowerMax.Ph.Power.Value) < Math.Abs(Fractals1[0].Ph.Power.Value))
          FractalLastPowerMax.Ph.Power = Fractals1[0].Ph.Power;
        var powerLast = _ticks.Where(t => t.Ph.Work.HasValue).Last().Ph.Work.Value;
        fractalPowerAverage = Lib.CMA(fractalPowerAverage, _ticks.TradesPerMinute(Fractals1[0], Fractals1[1]) / 2, powerLast);
        //fractalPowerAverage = Lib.CMA(fractalPowerAverage, _ticks.TradesPerMinute(Fractals1[0], Fractals1[1]) / 2, Fractals1[0].Ph.Power.Value);
        #endregion

        //IsPowerDown = true;// Math.Abs(powerLast) < Math.Abs(fractalPowerAverage.Value);
        //IsPowerDown = Math.Abs(Fractals1[0].Ph.Power.Value) < Math.Abs(fractalPowerAverage.Value);
        var powerRatio = Math.Abs((Fractals1[0].Ph.Power / Fractals1[2].Ph.Power).Value);// posByPriceLastLeg >= .30;//  && (tradeLeg >= CorridorSpreadMinimumInPips || posBS >= 1.15);
        var mptpmPrev = (Fractals1.Length > 4 ? new[] { Fractals1[2], Fractals1[4] } : Fractals1.Skip(1).Where(f => f.Ph.MassPerTradesPerMinute.HasValue))
          .Average(f => f.Ph.MassPerTradesPerMinute);
        var baseMass = bsBaseFoo(f=>f.Ph.MassPerTradesPerMinute);
        var massRatio = (Fractals1[0].Ph.MassPerTradesPerMinute / baseMass).Value;
        this.baseHeight = bsBaseFoo(f => f.Ph.Height.Abs());
        var heightRatio = Fractals1[0].Ph.Height.Abs() / baseHeight;
        var posBS = !ui.massOrHeight.HasValue ? massRatio * heightRatio : ui.massOrHeight.Value ? massRatio : heightRatio;
        var posBSMin = ui.mass1Mass0TradeRatio;
        this.isMassOk = massRatio >= posBSMin;
        this.isLegOk = heightRatio >= posBSMin;
        this.posBS = Lib.CMA(this.posBS, 0, cmaPeriod, posBS.Value);
        this.isPosBSOk = this.posBS >= posBSMin;

        #region Show Stats
        ret.Add("LegUA : " + legUpAverageInPips.ToString("n1"));
        ret.Add("LegDA : " + legDownAverageInPips.ToString("n1"));
        ret.Add("LegAA : " + string.Format("{0:n1}/{1:n1}/{2:n1}", fw.InPips(legAllAverage), fw.InPips(legAllAverageStats.AverageUp), fw.InPips(legAverageBottom)));
        //ret.Add("LegTA : " + legTradeAverage);
        ret.Add("LegPrc: " + fw.InPips(legPrice, 1).ToString("n1"));
        ret.Add("CorSM : " + CorridorSpreadMinimumInPips.ToString("n1"));
        ret.Add("FtlTm : " + Fractals.Where(f=>f.Ph.Time.HasValue).Average(f=>f.Ph.Time.Value.TotalMinutes).ToString("n1"));
        ret.Add("PosMas: " + string.Format("{0:p1}>{1:n1}", massRatio, fw.InPips(baseMass)));
        ret.Add("PosHgh: " + string.Format("{0:p1}>{1:n1}", heightRatio, fw.InPips(baseHeight)));
        ret.Add("PosM*H: " + string.Format("{0:p1}", heightRatio * massRatio));
        ret.Add("PosBS: " + string.Format("{0:p1}", posBS));
        //ret.Add("PwrL/A: " + PowerInPips(powerLast, 1) + "/" + PowerInPips(fractalPowerAverage.Value, 1));
        //ret.Add("PwrL/A: " + PowerInPips(Fractals1[0].Ph.Power.Value, 1) + "/" + PowerInPips(fractalPowerAverage.Value, 1));
        //ret.Add("OvlPAS: " + OverlapAverageShortPower);
        //ret.Add("OvlPL : " + OverlapLastPower);
        //ret.Add("OvlPA : " + OverlapAveragePower);
        //ret.Add("PosLeg : " + posBSByLeg.ToString("p1"));
        //ret.Add("PosPr : " + posByPrice.ToString("p1"));
        //ret.Add("PosPrL: " + posByPriceLastLeg.ToString("p1"));
        //ret.Add("WS.Avg: " + fw.InPips(WaveStats.Average).ToString("n1"));
        //ret.Add("WS.StD: " + fw.InPips(WaveStats.StDev).ToString("n1"));
        //ret.Add("WS.Nvg: " + fw.InPips(WaveStats.AverageN).ToString("n1"));
        //ret.Add("MT.Avg : " + string.Format("{0:n0}/{1:n1}", fw.InPips(massStats.Average), fw.InPips(tpmStats.Average)));
        //ret.Add("MT.StD : " + string.Format("{0:n0}/{1:n1}", fw.InPips(massStats.StDev), fw.InPips(tpmStats.StDev)));
        //ret.Add("MT.Min : " + string.Format("{0:n0}/{1:n1}", fw.InPips(massMinimum), fw.InPips(tpmMinimum)));
        //ret.Add("Period: " + wavePeriod.TotalMinutes.ToString("n1"));
        //ret.Add("Per.Up: " + waveUpsPeriod.TotalMinutes.ToString("n1"));
        //ret.Add("Per.Dn: " + waveDownsPeriod.TotalMinutes.ToString("n1"));
        #endregion

        FractalStats = string.Join(Environment.NewLine, ret.ToArray());
      } finally {
        RaisePropertyChanged(() => CorridorSpreadInPips);
        RaisePropertyChanged(() => FractalWavesText, () => FractalWaveColor
          , () => OverlapLast, () => OverlapLastPerc
          , () => OverlapAverageShortPower, () => FractalStats
          , () => WaveHeightInPips, () => OverlapStDev, () => OverlapAverage, () => OverlapAverageShort);
      }
    }

    #region GetMinutesBack
    void GetMinutesBack() {
      var sw = Stopwatch.StartNew();
      RaisePropertyChanged(() => CorridorHeightMinutesBySchedule);
      DateTime dTotal = DateTime.Now;
      if (Ticks.Count == 0) return;
      Action<Rate, double> writeTo = (t, p) => t.PriceAvg4 = p;
      GetPositionByFractals();
      if (Fractals.Length == 0) return;
      sw.Reset(); sw.Start();
      var fractalEndDate = Fractals.OrderBarsDescending().Take(ui.wavesCountSmall).Min(f => f.StartDate).AddMinutes(-CorridorHeightMinutesBySchedule);
      MinutesBackSampleCount = (ServerTime - fractalEndDate).TotalMinutes.ToInt();
      Timeframe = TimeframeAlt = fractalEndDate;
      RegressionCoefficients = SetTicksPrice(
        _ticks.Where(t => t.StartDate >= fractalEndDate).GroupTicksToRates(), 1, readFrom, writeTo);
      ShowTicks();
      sw.Reset(); sw.Start();
      decisionTime = DateTime.Now - dTotal;
      MinutesBackSpeed = decisionTime.TotalSeconds;
    }

    void GetMinutesBack_() {
      RaisePropertyChanged(() => CorridorHeightMinutesBySchedule);
      DateTime dTotal = DateTime.Now;
      DateTime ret = timeFrameDateStart;
      var wi = new List<WaveInfo>();
      if (Ticks.Count == 0) return;
      var ticksReversed = TicksForTimeFrame.OrderBarsDescending().ToList();
      Action<Rate, double> writeTo = (t, p) => t.PriceAvg4 = p;
      #region Minutes By Waves
      //try {
      //  SetTicksPrice(ticksCopy, 8, (tick, price) => tick.PriceWave = price);
      //  var waves = Signaler.GetWaves(ticksCopy.Select((t, ti) => new Signaler.DataPoint(t.PriceWave, t.StartDate, ti)).ToArray()).ToArray();
      //  waves = waves.OrderByDescending(w => w.Date).Take(2).ToArray();
      //  var waveDateStart = waves.Last().Date;
      //  Dispatcher.Invoke(DispatcherPriority.Send, new Action(() => {
      //    txtTimeFrameMinutesMinimum.Text = (ServerTime - waveDateStart).TotalMinutes.ToInt() + "";
      //  }));
      //} catch (Exception exc) { Log = exc; }
      #endregion
      Func<double, double, double> fooCorrHeightMinimum =
        (priceHigh, priceLow) => (priceHigh - priceLow) * (ui.corridorMinimumPercent / 100.0);
      var corridorHeightMin = 0.0;
      Func<double, double, bool> fooPosTradeCondition = NewMethod();
      var ticks = new List<Rate>();
      var minimumTime = ServerTime.AddMinutes(-Math.Max(TimeframeByTicksMin / 10.0, ui.timeFrameMinutesMinimum));
      {
        //var priceHighTime = doTicks ? ticksReversed.Take(10).Last().StartDate : Ticks.Max(t => t.StartDate).AddMinutes(-3);
        //var tickHT = Ticks.Where(t => t.StartDate > priceHighTime);
        GetPositionByFractals();
        priceHeightMax = Ticks.Where(t => t.StartDate >= FractalSell.StartDate).Max(readFrom); //tickHT.Max(readFrom);
        priceHeightMin = Ticks.Where(t => t.StartDate >= FractalBuy.StartDate).Min(readFrom);//tickHT.Min(readFrom);
      }
      if (!ui.cachePriceHeight || priceHeightMax != priceHeightMaxOld || priceHeightMin != priceHeightMinOld) {
        var lastDate = DateTime.MaxValue;
        TimeSpan interval = TimeSpan.Zero;
        List<ManualResetEvent> mreList = new List<ManualResetEvent>();
        int skipMinutes = 0;
        GetPositionByFractals();

        var tsiWave = Fractals.Skip((-ui.wavesCountBig) - 1).FirstOrDefault();
        if (tsiWave == null || Fractals.Length < 3) {
          if (ui.wavesCountBig < 0)
            FractalWaveColor = false;
          else {
            Log = "WaveCount < 2";
            Timeframe = ServerTime;
          }
          return;
        } else FractalWaveColor = true;

        var intervalMinutesOffset = (ServerTime - tsiWave.StartDate).TotalMinutes;
        var tsiStartDate = tsiWave.StartDate.AddMinutes(-CorridorHeightMinutesBySchedule / ui.minumumTimeByFractalWaveRatio);
        var fractalEndDate = ui.wavesCountBig < 0 && Fractals.Length >= ui.wavesCountSmall ?
          Fractals[ui.wavesCountSmall - 1].StartDate.AddMinutes(-CorridorHeightMinutesBySchedule) : DateTime.MinValue;
        #region Iterate
        foreach (var tick in ticksReversed) {
          ticks.Insert(0, tick);
          if (!tick.StartDate.Between(fractalEndDate, tsiStartDate)) continue;
          var tickLast = ticks.Last();
          interval = TimeSpan.FromMinutes(
            ((tickLast.StartDate - tick.StartDate).TotalMinutes - intervalMinutesOffset) * ui.fractalPadding * 2
            + skipMinutes);
          skipMinutes = 0;
          //var intervalInMinutes = TimeSpan.FromMinutes(Math.Pow((tickLast.StartDate - tick.StartDate).TotalMinutes * ui.timeFramePercInterval, ui.timeFramePercSmooth) / 100.0);
          if (tick.StartDate > minimumTime || (lastDate - tick.StartDate) < interval) continue;
          lastDate = tick.StartDate;
          ManualResetEvent doneEvent = new ManualResetEvent(false);
          mreList.Add(doneEvent);
          var workObject = new object[] {
            tick,
            doneEvent,
            ticks,//.Select(r=>r.Clone()).ToArray(),
            writeTo,
            tickLast,
            wi
          };

          #region delegate
          if (false) {
            WaitCallback run = (o) => {
              var paramArray = ((object[])o);
              var tickLocal = paramArray[0] as Rate;
              var eventLocal = paramArray[1] as ManualResetEvent;
              var ticksCopy = paramArray[2] as Rate[];
              try {
                while (true) {
                  var coeffs = SetTicksPrice(ticksCopy, 1, readFrom, writeTo);
                  var tickHigh = ticksCopy.OrderByDescending(t => t.PriceHigh).First();
                  var tickLow = ticksCopy.OrderBy(t => t.PriceLow).First();
                  var vu = GetVolatilityUp(ticksCopy.ToList(), ui.volatilityWieght);
                  var vd = GetVolatilityDown(ticksCopy.ToList(), ui.volatilityWieght);
                  var vh = vu + vd;
                  ////if (vh < corridorSpreadMinimum) break;
                  var priceHeightUp = priceHeightMax - tickLast.PriceAvg4;
                  var priceHeightDown = tickLast.PriceAvg4 - priceHeightMin;
                  ////corridorHeightMin = fooCorrHeightMinimum(tickHigh.PriceHigh, tickLow.PriceLow);
                  ////if (Math.Min(vd, vu) < corridorHeightMin) break;
                  var upDownRatio = Lib.FibRatio(vu, vd);
                  var posBuy = /*Math.Abs(*/priceHeightDown / vd - 1/*)*/;
                  var posSell = /*Math.Abs(*/priceHeightUp / vu - 1/*)*/;
                  var poss = new[] { posBuy, posSell };
                  //var posTrade = vd == vu || !ui.corridorByMinimumVolatility.HasValue ? Math.Min(posSell, posBuy) : fooPosTradeCondition(vd, vu) ? posBuy : posSell;
                  var posTrade = vd == vu || !ui.corridorByMinimumVolatility.HasValue ? Lib.FibRatio(posBuy, posSell) : fooPosTradeCondition(vd, vu) ? posBuy : posSell;
                  var stDev = ticksCopy.StdDev(t => readFrom(t) - t.PriceAvg4);
                  var waveRatio = stDev / (vd + vu);// Math.Min(vd, vh) / (tickHigh.PriceClose - tickLow.PriceClose);
                  lock (wi) {
                    wi.Add(new WaveInfo(tickLocal.StartDate, Math.Round(waveRatio, 3), Math.Round(posTrade, 2), upDownRatio, /*Math.Abs(vu / vd - vd / vu)*/ vu, vd, coeffs, interval));
                  }
                  break;
                }
              } catch (Exception exc) {
                Log = exc;
              }
              eventLocal.Set();
            };
          }
          #endregion

          CalculateTimeFrame(workObject); //          
          //ThreadPool.QueueUserWorkItem(CalculateTimeFrame, workObject);
          if (mreList.Count == 64) {
            WaitHandle.WaitAll(mreList.ToArray(), 1000 * 10);
            mreList.Clear();
          }
          //System.Diagnostics.Debug.WriteLine(
          //  "GetMinutesBack:" + tick.StartDate.ToShortTimeString() + ":" + Math.Round(waveRatio, 2) + ":" + Math.Round(posTrade, 2) + ":" + (DateTime.Now - d).TotalMilliseconds);
        }
        #endregion
        if (mreList.Count > 0) WaitHandle.WaitAll(mreList.ToArray(), 1000 * 10);
        //var wisUpDown = wi.OrderBy(w => Math.Abs(w.UpDownRatio) > .1).OrderBy(w => w.UpDownRatio).ThenByDescending(w => w.WaveRatio).ThenBy(w => w.StartDate).ToArray();
        var wisUpDown = wi./*OrderBy(w => Math.Ceiling(w.UpDownRatio * 10) / 10.0).*/OrderByDescending(w => Math.Abs(w.TradePosition)).ThenBy(w => w.StartDate).ToArray();
        var wisWaveRatio = wi./*OrderBy(w => Math.Ceiling(w.UpDownRatio * 10) / 10.0).*/OrderByDescending(w => w.WaveRatio).ThenBy(w => w.TradePosition).ThenByDescending(w => w.StartDate).ToArray();
        if (wisUpDown.Length == 0) {
          MinutesBackSampleCount = 0;
        } else {
          priceHeightMaxOld = priceHeightMax; priceHeightMinOld = priceHeightMin;
          if (ui.corridorByUpDownRatio) {
            Timeframe = ret = wisUpDown.First().StartDate;
            TimeframeAlt = wisWaveRatio.First().StartDate;
            RegressionCoefficients = wisUpDown.First().Coeffs;
            MinutesBackSampleCount = wisUpDown.Length;
          } else {
            Timeframe = ret = wisWaveRatio.First().StartDate;
            TimeframeAlt = wisUpDown.First().StartDate;
            RegressionCoefficients = wisWaveRatio.First().Coeffs;
            MinutesBackSampleCount = wisWaveRatio.Length;
          }
          if (ret > DateTime.MinValue) {
            ticks = TicksForTimeFrame.Where(t => t.StartDate >= ret).ToList();
            SetTicksPrice(ticks, 1, readFrom, writeTo);
            VolatilityUp = GetVolatilityUp(ticks, ui.volatilityWieght1);
            VolatilityDown = GetVolatilityDown(ticks, ui.volatilityWieght1);
            VolatilityUpPeak = GetVolatilityUp(ticks, 0);
            VolatilityDownPeak = GetVolatilityDown(ticks, 0);
          }
          ShowTicks();
        }
        decisionTime = DateTime.Now - dTotal;
        System.Diagnostics.Debug.WriteLine("GetMinutesBack Total:" + decisionTime.TotalMilliseconds);
        MinutesBackSpeed = decisionTime.TotalSeconds;
      }
    }
    #endregion

    private Func<double, double, bool> NewMethod() {
      return (d, u) => ui.corridorByMinimumVolatility.Value ? d < u : d > u;
    }
    TimeSpan decisionTime = TimeSpan.Zero;

    #region GetVolatility ========================================
    static double GetVolatilityUp(List<Rate> ticks, int weight) {
      return GetVolatility(ticks, t => t.BidHigh - t.PriceAvg4, weight);
    }
    static double GetVolatilityDown(List<Rate> ticks, int weight) {
      return GetVolatility(ticks, t => t.PriceAvg4 - t.AskLow, weight);
    }
    static double
      GetVolatility(List<Rate> ticks, Func<Rate, double> heightLambda, int weight) {
      if (weight == 0) return ticks.Max(heightLambda);
      double volatility = 0;
      Func<Rate, bool> whereLambda = t => heightLambda(t) > volatility;
      var volTicks = ticks.Where(whereLambda).ToArray();
      double tc = volTicks.Length;
      while (volTicks.Length / tc > weight / 100.0) {
        volatility = volTicks.Average(heightLambda);
        volTicks = volTicks.Where(whereLambda).ToArray();
      }
      return volatility;
    }
    #endregion ========================================================

    Rate[] _ticksInTimeFrame = new Rate[] { };
    double GetPriceHeight1(Rate rate) {
      //return rate.PriceHigh > rate.PriceAvg1 ? rate.PriceHigh - rate.PriceAvg1 : rate.PriceLow - rate.PriceAvg1;
      var priceHeightAverage = new[] { priceHeightMax, priceHeightMin }.Average();
      return rate.PriceAvg > priceHeightAverage ? priceHeightMax - rate.PriceAvg1 : priceHeightMin - rate.PriceAvg1;
    }
    double GetPriceHeight4(Rate rate) {
      return rate.PriceHigh > rate.PriceAvg4 ? rate.PriceHigh - rate.PriceAvg4 : rate.PriceLow - rate.PriceAvg4;
    }
    private Rate[] TicksInTimeFrame {
      get {
        {
          if (true || _ticksInTimeFrame.Length == 0) {
            var logHeader = "TIF "; var dateNow = DateTime.Now; int step = 0; Func<string> timeSpan = () => logHeader + " : " + (step++) + " " + (DateTime.Now - dateNow).TotalMilliseconds;
            if (Ticks == null || Ticks.Count == 0) return _ticksInTimeFrame;
            var ticks = _ticks.Where(t => t.StartDate >= Timeframe).ToList();
            if (ticks.Count == 0) return _ticksInTimeFrame;
            //if (ticks.Count < ui.ticksBack) ticks = Ticks.Reverse<Rate>().Take(ui.ticksBack).Reverse().ToList();
            SetTicksPrice(ticks, 1, r => r.PriceAvg, (tick, price) => tick.PriceAvg1 = price);
            //SetTicksPrice(ticks, RegressionCoefficients, (tick, price) => tick.PriceAvg1 = price);

            VolatilityUp = GetVolatility(ticks, t => t.BidHigh - t.PriceAvg1, ui.volatilityWieght1);
            VolatilityDown = GetVolatility(ticks, t => t.PriceAvg1 - t.AskLow, ui.volatilityWieght1);
            VolatilityUpPeak = GetVolatility(ticks, t => t.BidHigh - t.PriceAvg1, 0);
            VolatilityDownPeak = GetVolatility(ticks, t => t.PriceAvg1 - t.AskLow, 0);

            var tickLast = ticks.Last();
            PriceHeight = GetPriceHeight1(tickLast);
            if (drawWaves) {
              SetTicksPrice(ticks, ui.wavePolynomeOrder, r => r.PriceAvg, (tick, price) => tick.PriceAvg2 = price);
              waves = Signaler.GetWaves(ticks.Select((t, ti) => new Signaler.DataPoint(t.PriceAvg2, t.StartDate, ti)).ToArray());
            }
            #region Save to file
            if (ui.SaveVoltageToFile) {
              if (SaveToFileScheduler == null) SaveToFileScheduler = new ThreadScheduler(TimeSpan.FromSeconds(1), (s, e) => Log = e.Exception);
              if (!SaveToFileScheduler.IsRunning)
                SaveToFileScheduler.Command = () => {
                  var s = new StringBuilder();
                  ticks.OrderBars().ToList().ForEach(v => s.Append(v.StartDate + "," + (v.PriceAvg - v.PriceAvg1) + "," + v.PriceAvg + "," + v.PriceAvg1 + Environment.NewLine));
                  System.IO.File.WriteAllText("C:\\Volts.csv", s.ToString());
                };
            }
            #endregion
            _ticksInTimeFrame = ticks.ToArray();
            RaisePropertyChanged(() => WavesRatio);
            VLog = timeSpan();
            //var voltsDateStart = ServerTime.AddMinutes(-15);
            //Voltages = Signaler.GetVoltageByTick(_ticksInTimeFrame.Where(t => t.StartDate > voltsDateStart), 10);
          }
          return _ticksInTimeFrame;
        }
      }
    }
    static private double[] Regress(double[] prices, int polyOrder) {
      var coeffs = new[] { 0.0, 0.0 };
      Lib.LinearRegression(prices, out coeffs[1], out coeffs[0]);
      return coeffs;
      return Regression.Regress(prices, polyOrder);
    }
    static void SetTicksPrice(IEnumerable<Rate> ticks, double[] coeffs, Action<Rate, double> a) {
      double[] yy = new double[ticks.Count()];
      int i1 = 0;
      foreach (var tick in ticks) {
        double y1 = 0; int j = 0;
        coeffs.ToList().ForEach(c => y1 += coeffs[j] * Math.Pow(i1, j++));
        a(tick, y1);// *poly2Wieght + y2 * (1 - poly2Wieght);
        yy[i1++] = y1;
      }
    }
    static double[] SetTicksPrice(IEnumerable<Rate> ticks, int polyOrder, Func<Rate, double> readFrom, Action<Rate, double> writeTo) {
      var coeffs = Regress(ticks.Select(readFrom).ToArray(), polyOrder);
      SetTicksPrice(ticks, coeffs, writeTo);
      return coeffs;
    }

    void CleanTicks() {
      var dateClear = timeFrameDateStart;
      _ticks.Where(t => t.StartDate < dateClear).ToList().ForEach(t => _ticks.Remove(t));
    }
    void GetVolatility() {
      if (ui.timeFrameMinutesMaximum / 60.0 > ui.highMinutesHoursBack)
        RatesHigh = Ticks;
      else {
        var startTime = ServerTime.AddHours(-ui.highMinutesHoursBack);
        var endTime = RatesHigh.Count > 0 ? RatesHigh.First().StartDate.AddHours(-ui.highMinutesHoursBack) : DateTime.FromOADate(0);
        RatesHigh.Where(r => r.StartDate < endTime).ToList().ForEach(r => RatesHigh.Remove(r));
        endTime = ServerTime.Round(1).AddMinutes(-1);
        do {
          fw.GetBars(1, startTime, endTime, ref RatesHigh);
          if (RatesHigh.Count > 0 && (RatesHigh.Max(r => r.StartDate) - RatesHigh.Min(r => r.StartDate)).TotalHours >= ui.highMinutesHoursBack) break;
          startTime = startTime.AddHours(-1);
        } while (true);
      }
    }
    #endregion

    #region ProcessPrice
    O2G.Price priceCurrent;
    Rate lastBar = new Rate();
    void ProcessPrice(Order2GoAddIn.Price price, ref List<Rate> ticks) {
      try {
        var serverTime = ServerTime;
        RaisePropertyChanged(() => ServerTime);
        #region Init Ticks
        if (ticks == null || ticks.Count == 0) {
          if (!TicksScheduler.IsRunning && !TestMode) {
            GetTicksAsync(fxDateNow, fxDateNow);
            Log = "Loading ticks.";
          } else if (TestMode) GetTicksAsync();

          return;
        }
        #endregion

        #region Run Calculator
        CleanTicks();
        SpreadAverage = SpreadByBarPeriod(1, false);
        SpreadAverageShort = SpreadByBarPeriod(1, true);
        SpreadAverage5Min = SpreadByBarPeriod(5, false);
        SpreadAverage10Min = SpreadByBarPeriod(10, false);
        SpreadAverage15Min = SpreadByBarPeriod(15, false);

        #endregion

        #region Do Price
        if (price != null && !TestMode) {
          priceCurrent = price;
          var priceTime = price.Time > serverTime.AddMinutes(10) ? price.Time.AddHours(-1) : price.Time;
          RunMinutesBack(price);
          if (doTicks) {
            ticks.Add(new Tick(serverTime, price.Ask, price.Bid, 0, false));
            ////Ticks.FillRSI(14, getPrice, getRsi, setRsi);
            GetTicksAsync(ticksStartDate, fxDateNow);
          } else {
            var lastTickTime = serverTime.Round().AddMinutes(-1);
            if ((lastTickTime - _ticks.Last().StartDate).TotalMilliseconds > 0)
              GetTicksAsync(ticksStartDate, lastTickTime);
            if (lastBar.StartDate.AddMinutes(1) < serverTime)
              lastBar = new Rate(false);
            lastBar.AddTick(priceTime.Round(), price.Ask, price.Bid);
          }
        }
        #endregion
      } catch (Exception exc) { Log = exc; }
    }
    void RunMinutesBack(O2G.Price price) {
      if (!MinutesBackScheduler.IsRunning)
        MinutesBackScheduler.Command = () => {
          GetMinutesBack();
          RunDecider(price);
        };
    }
    void RunDecider(O2G.Price price) {
      DecisionScheduler.Command = () => {
        buySellSignals.Keys.Where(tr => (ServerTime - tr.serverTime).TotalMinutes > 1).ToList()
          .ForEach(tr => buySellSignals.Remove(tr));
        foreach (var bs in buySellSignals)
          Decisioner(price, bs.Key, bs.Value);
      };
    }
    void ShowTicks() {
      if (!CorridorsScheduler.IsRunning)
        CorridorsScheduler.Command = () => {
          if (TicksInTimeFrame.Length < 10) return;
          var wave1 = waves.OrderByDescending(w => w.Date).FirstOrDefault() ?? new Signaler.DataPoint();
          var wave2 = waves.OrderByDescending(w => w.Date).Skip(1).FirstOrDefault() ?? wave1;
          CorridorsWindow_EURJPY.AddTicks(
            null,
            TicksInTimeFrame.ToList(),
            Voltages,
            PeakVolt.AverageAsk,
            ValleyVolt.AverageBid, 0, 0, 0, 0,
            Fractals.Length > 0 ? Fractals[0].StartDate : DateTime.MinValue, Fractals.Length > 1 ? Fractals[1].StartDate : DateTime.MinValue,
            //PeakVolt.StartDate, ValleyVolt.StartDate, 
            new[] { 0.0 });
        };
    }
    #endregion

    #region Decisioner

    public TradeStatistics GetTradingStatistics(TradeRequest tradeRequest, TradeStatistics tradeStats, double positionBuy, double positionSell) {
      var cs = CorridorSpreadInPips;
      tradeStats.positionBuy = positionBuy;
      tradeStats.positionSell = positionSell;
      tradeStats.spreadAverage = SpreadAverage;
      tradeStats.spreadAverage5Min = SpreadAverage5Min;
      tradeStats.spreadAverage10Min = SpreadAverage10Min;
      tradeStats.spreadAverage15Min = SpreadAverage15Min;
      tradeStats.spreadAverageHighMin = SpreadByBarPeriod(tradeRequest.highBarMinutes, false);
      tradeStats.voltPriceMax = VoltPriceHigh;
      tradeStats.voltPriceMin = VoltPriceLow;
      tradeStats.ticksPerMinuteAverageShort = TicksPerMinuteAverage;
      tradeStats.ticksPerMinuteAverageLong = TicksPerMinuteAverageLong;
      tradeStats.corridorMinimum = SpreadByBarPeriod(tradeRequest.corridorMinites, false);
      tradeStats.corridorSpread = CorridorSpread(tradeRequest.doTrend);
      tradeStats.voltsAverage = Voltages.Count == 0 ? 0 : Voltages.Average(v => v.Volts);
      tradeStats.peakVolts = PeakVolt.Volts;
      tradeStats.valleyVolts = ValleyVolt.Volts;
      tradeStats.timeFrame = TimeframeInMinutes;
      tradeStats.Angle = Angle;
      return tradeStats;
    }

    //[MethodImpl(MethodImplOptions.Synchronized)]
    Dictionary<TradeRequest, TradeResponse> buySellSignals = new Dictionary<TradeRequest, TradeResponse>();
    public TradeResponse Decisioner(TradeRequest tr) {
      #region Run Voltages
      if (!tr.doTrend && !VoltageScheduler.IsRunning)
        VoltageScheduler.Command = () => {
          DateTime vDate = DateTime.Now;
          Voltages = Signaler.FindMaximasPeakAndValley(TicksInTimeFrame, 10, true, ref PeakVolt, ref ValleyVolt);
          #region Save to File
          if (false && ui.SaveVoltageToFile)
            try {
              var s = "";
              Voltages.OrderBy(v => v.StartDate).ToList().ForEach(v => s += v.StartDate + "," + v.VoltsCMA + "," + v.PriceAvg + "," + v.PriceAvg1 + Environment.NewLine);
              System.IO.File.WriteAllText("C:\\Volts.csv", s);
            } catch { }
          #endregion
          RaisePropertyChanged(() => VoltageSpread, () => VoltageSpreadInPips, () => VoltPriceHigh, () => VoltPriceLow, () => PeakPriceHigh, () => ValleyPriceLow);
          VLog = "Volt's time: " + (DateTime.Now - vDate).TotalMilliseconds + " ms. PeakVolt:" + PeakVolt.AverageAsk.ToString("n3") + "@" + PeakVolt.StartDate.ToShortTimeString() + " - ValleyVolt:" + ValleyVolt.AverageBid.ToString("n3") + "@" + ValleyVolt.StartDate.ToShortTimeString();
        };
      #endregion

      if (!buySellSignals.ContainsKey(tr))
        buySellSignals.Add(tr, new TradeResponse());
      try {
        RidOfOldPositions(tr, buySellSignals[tr]);
      } catch { return null; }
      var isReady = !MinutesBackScheduler.IsRunning;
      var trKey = buySellSignals.Keys.Where(k => k == tr).FirstOrDefault();
      if (trKey == null) return null;
      trKey.serverTime = tr.serverTime;
      var response = buySellSignals[tr];
      if (tr.tradesBuy.Length == 0 && response.GoBuy &&
        tr.tradesSell.Length > tr.tradeOnProfitAfter && !tr.tradesSell.Any(t => t.PL > 0))
        response.GoBuy = false;
      if (tr.tradesSell.Length == 0 && response.GoSell &&
        tr.tradesBuy.Length > tr.tradeOnProfitAfter && !tr.tradesBuy.Any(t => t.PL > 0))
        response.GoSell = false;
      if (tr.tradesBuy.Any(t => t.PL > 0)) response.GoBuy = false;
      if (tr.tradesSell.Any(t => t.PL > 0)) response.GoSell = false;
      //if (tr.closeAllOnTrade && tr.tradesBuy.Length > 0) response.GoBuy = false;
      //if (tr.closeAllOnTrade && tr.tradesSell.Length > 0) response.GoSell = false;

      response.IsReady = isReady;
      return response;
    }

    bool? _fractalWaveBuySellColor;
    public bool? FractalWaveBuySellColor {
      get { return _fractalWaveBuySellColor; }
      set { _fractalWaveBuySellColor = value; RaisePropertyChangedCore(); }
    }
    public bool FractalBuyInRange { get; set; }
    public bool FractalSellInRange { get; set; }
    public double PricePosition { get; set; }
    public bool IsPriceInPosition { get; set; }
    public void Decisioner(Order2GoAddIn.Price eventPrice, TradeRequest tr, TradeResponse ti) {
      try {
        var price = eventPrice ?? priceCurrent;

        var logHeader = "D"; var dateNow = DateTime.Now; Func<string, string> timeSpan = step => logHeader + " : " + (DateTime.Now - dateNow).TotalMilliseconds + " - " + step;
        if (fw == null || fw.Desk == null || FractalBuy == null || TestMode) return;
        VLog = timeSpan("Start");
        var ticksInTimeFrame = TicksInTimeFrame;
        VLog = timeSpan("Ticks");

        var angleCanBuy = Angle.Between(-tr.tradeAngleMax, -tr.tradeAngleMin);
        var angleCanSell = Angle.Between(tr.tradeAngleMin, tr.tradeAngleMax);

        var fractalWaveCanBuy = Fractals.Length > 1 && !isInSell && angleCanBuy;
        //(price.Bid <= Fractals[2].AskHigh 
        //|| (angleCanBuy && Fractals.Length > 3 && Fractals[0].AskHigh >= Fractals[3].BidLow));
        var fractalWaveCanSell = Fractals.Length > 1 && isInSell && angleCanSell;
        //(price.Ask >= Fractals[2].BidLow 
        //|| (angleCanSell && Fractals.Length > 3 && Fractals[0].BidLow <= Fractals[3].AskHigh));

        FractalWaveBuySellColor = fractalWaveCanBuy ? true : fractalWaveCanSell ? (bool?)false : null;


        if (ticksInTimeFrame.Length == 0 || MinutesBackSampleCount == 0) {
          ti.CorridorOK = false;
          IsBuyMode = null;
          return;
        }
        ShowTicks();

        #region Buy/Sell position
        bool canBuy = false, canSell = false;

        Func<double, double> volatilityLambda = v => tr.tradeByVolatilityMaximum.HasValue ? tr.tradeByVolatilityMaximum.Value ? VolatilityMaxInPips : VolatilityAvgInPips : v;
        var positionBuy = !tr.doTrend ? fw.InPips(VolatilityUp - price.Ask, 1) :
          posBS * (!isInSell ? 100 : 0);
        //-PriceHeightInPips - volatilityLambda(VolatilityDownInPips);

        var positionSell = !tr.doTrend ? fw.InPips(price.Bid - ti.TradeStats.peakPriceHigh, 1) :
          posBS * (isInSell ? 100 : 0);
        //PriceHeightInPips - volatilityLambda(VolatilityUpInPips);
        #endregion


        ti.TradeStats = GetTradingStatistics(tr, ti.TradeStats, positionBuy, positionSell);

        #region Density Functions
        var densityFoo_0 = new Func<bool, double>((buy) => {
          return Math.Round(Math.Max(Math.Max(PeakVolt.AverageAsk - ValleyVolt.AverageBid, fw.GetMaxDistance(buy)) / fw.PointSize, SpreadAverage5MinInPips), 1);
        });
        var densityFoo_1 = new Func<bool, double>((buy) => {
          return fw.InPips(Math.Max(fw.GetMaxDistance(buy), SpreadByBarPeriod(tr.highBarMinutes, false)), 1);
        });
        var densityFoo_2 = new Func<bool, double>((buy) => {
          return fw.InPips(ti.TradeStats.spreadAverageHighMin, 1);
        });
        var densityFoo_3 = new Func<bool, double>((buy) => {
          return fw.InPips(Math.Max(CorridorSpread(tr.doTrend), SpreadAverage5Min), 1);
        });
        var densityFoo_4 = new Func<bool, double>((buy) => {
          return fw.InPips(Math.Max(Math.Abs(VolatilityUpPeak), Math.Abs(VolatilityDownPeak)), 1);
        });
        var densityFoo_5 = new Func<bool, double>((buy) => {
          return fw.InPips((Math.Abs(VolatilityUpPeak) + Math.Abs(VolatilityDownPeak)) / 2, 1);
        });
        var densityFoo_6 = new Func<bool, double>((buy) => {
          return fw.InPips(Math.Min(Math.Abs(VolatilityUpPeak), Math.Abs(VolatilityDownPeak)), 1);
        });
        var densityFoo_7 = new Func<bool, double>((buy) => {
          return buy ? legDownAverageInPips : legUpAverageInPips;// CorridorSpreadInPips;
        });
        var densityFoo_8 = new Func<bool, double>((buy) => {
          return fw.InPips(Math.Max(tr.closeOnProfitOnly ? 0 : CorridorSpreadMinimum, buy ? VolatilityDownPeak : VolatilityUpPeak), 1);
        });
        var densityFoos = new[] { densityFoo_0, densityFoo_1, densityFoo_2, densityFoo_3, densityFoo_4, densityFoo_5, densityFoo_6, densityFoo_7, densityFoo_8 };
        #endregion

        #region GoTrade Functions
        var isDirectionOk = new Func<bool, bool>(buy => !tr.tradeByDirection ? true : buy ? price.AskChangeDirection > 0 : price.BidChangeDirection < 0);
        var goTrade_0 = new Func<double, bool, bool>((position, buy) => position.Between(SpreadAverageShortInPips / 2, SpreadAverageShortInPips));
        var goTrade_1 = new Func<double, bool, bool>((position, buy) => position.Between(0, SpreadAverageShortInPips / 2));
        var goTrade_2 = new Func<double, bool, bool>((position, buy) => position.Between(-SpreadAverageShortInPips / 2, 0));
        var goTrade_3 = new Func<double, bool, bool>((position, buy) => position.Between(-SpreadAverageShortInPips / 2, SpreadAverageShortInPips / 2));
        var goTrade_4 = new Func<double, bool, bool>((position, buy) => position.Between(-GetCorridorSpreadInPips(tr.doTrend) * .1, 0));
        var goTrade_5 = new Func<double, bool, bool>((position, buy) =>
          buy ? price.Ask.Between(ValleyPriceLow, ValleyPriceLowAverage) : price.Bid.Between(PeakPriceHighAverage, PeakPriceHigh)
          );
        var goTrade_6 = new Func<double, bool, bool>((position, buy) => {
          var h = Math.Max(tickHigh.PriceAvg2 - tickHigh.PriceAvg, tickLow.PriceAvg2 - tickLow.PriceAvg);
          return position.Between(h / 2, h);
        });
        var goTrade_7 = new Func<double, bool, bool>((position, buy) => position.Between(-SpreadAverageShortInPips, SpreadAverageShortInPips));
        var goTradeFoos = new[] { goTrade_0, goTrade_1, goTrade_2, goTrade_3, goTrade_4, goTrade_5, goTrade_6, goTrade_7 };
        #endregion

        bool goBuy = false, goSell = false;
        #region Action - decideByVoltage_11
        Action decideByVoltage_11 = () => {

          #region RSI
          if (false) {
            var ratesForRsi = Ticks.Where(t => t.StartDate >= ServerTime.AddMinutes(-16)).GetMinuteTicks(1);
            ratesForRsi.FillRsi(14, r => r.PriceClose);
            if (!ratesForRsi.Any(r => r.PriceRsi.HasValue)) return;
            ratesForRsi.Where(r => r.PriceRsi.HasValue).ToArray().CR(r => (double)r.PriceRsi, (Rate r, double d) => r.PriceRsiCR = d);
            RsiStdDev = ratesForRsi.StdDev(r => r.PriceRsi - r.PriceRsiCR);// (getRsi);
            RsiAverage = ti.RsiRegressionAverage = ratesForRsi.Last(r => r.PriceRsiCR.HasValue).PriceRsiCR.Value;// .Average(getRsi).Value;
            //RsiMaximum = ticksInTimeFrame.Max(getRsi).Value;
            //RsiMinimum = ticksInTimeFrame.Min(getRsi).Value;
            RsiOffset = RsiAverage - 50;

            //ti.RsiRegressionOffsetBuy = (rsiBuy.Last().Point1 - 50).ToInt();
            //ti.RsiRegressionOffsetSell = (rsiSell.Last().Point1 - 50).ToInt();
            RsiBuy = (RsiAverage - RsiStdDev).ToInt();
            ti.RsiRegressionOffsetBuy = tr.tradeByRsi ? RsiBuy : 0;
            RsiSell = (RsiAverage + RsiStdDev).ToInt();
            ti.RsiRegressionOffsetSell = tr.tradeByRsi ? RsiSell : 0;
            var ticksRsi = Ticks.Where(t => t.StartDate > ServerTime.AddMinutes(-5));
            ti.RsiLow = ticksRsi.Where(r => getRsi(r) > 0).Min(getRsi).Value.ToInt();
            ti.RsiHigh = ticksRsi.Max(getRsi).Value.ToInt();
            //var rsiCanBuy = ti.RsiLow <= (tr.rsiTresholdBuy + ti.RsiRegressionOffsetBuy);
            //var rsiCanSell = ti.RsiHigh >= (tr.rsiTresholdSell + ti.RsiRegressionOffsetSell);
          }
          #endregion

          var rsiCanBuy = !tr.tradeByRsi || ti.RsiLow.Between(0.01, ti.RsiRegressionOffsetBuy * 1.15);// && lastFractalBuy != null && lastFractalBuy.StartDate > ServerTime.AddMinutes(-5);
          var rsiCanSell = !tr.tradeByRsi || ti.RsiHigh >= ti.RsiRegressionOffsetSell * .85;// && lastFractalSell != null && lastFractalSell.StartDate > ServerTime.AddMinutes(-5); ;
          //Func<double, bool> closeToExtreamLambda = pe => Math.Abs(pe - readFrom(Ticks.Last())) < price.Spread;
          //fractalLast = ratesForRsi.HasFractal(true).Last();
          var fractalTrade = new[] { FractalSell, FractalBuy }.OrderBarsDescending().First();
          var fractalOposite = new[] { FractalSell, FractalBuy }.SingleOrDefault(f => f.Fractal != Fractals1[1].Fractal);
          var ticksAfterFractal = _ticks.Where(t => t.StartDate > Fractals[0].StartDate).ToArray();
          var priceExtream = isInSell ? ticksAfterFractal.Max(t => t.PriceHigh) : ticksAfterFractal.Min(t => t.PriceLow);
          var fractalRange = Math.Abs(Fractals[0].FractalPrice.Value - priceExtream) * ui.peakTradeMargin;// SpreadAverage * tr.tradeByFractalCoeff;
          var fractalSell = fractalTrade.HasFractalBuy;
          var fractalBuy = fractalTrade.HasFractalSell;
          if (fractalTrade.StartDate <= Fractals[0].StartDate) fractalSell = fractalBuy = false;
          IsBuyMode = !fractalBuy && !fractalSell ? (bool?)null : fractalSell;
          #region Breakthrough
          var breakThroughBuy = false;
          var breakThroughSell = false;
          if (false && Fractals.Length > 0) {
            var ticksSynceFractal = _ticks.Where(t => t.StartDate >= Fractals[0].StartDate).ToArray();
            var fractalsLocal = ticksSynceFractal.FindFractals(0, 2.0.FromMinutes(), 1, 100, b => b.PriceHigh, b => b.PriceLow);
            var fractalsLocalSell = fractalsLocal.Where(f => f.HasFractalSell).ToArray();
            var fractalsLocalBuy = fractalsLocal.Where(f => f.HasFractalBuy).ToArray();
            if (isInSell && fractalsLocalSell.Length > 0 && priceCurrent.Bid > fractalsLocalSell.Max(f => f.AskHigh))
              breakThroughBuy = true;
            if (!isInSell && fractalsLocalBuy.Length > 0 && priceCurrent.Ask < fractalsLocalBuy.Min(f => f.BidLow))
              breakThroughSell = true;
          }
          #endregion

          IsPowerDown = OverlapLast.TotalMinutes >= (OverlapAverageShort.TotalMinutes.Floor() + OverlapStDev.TotalMinutes.Floor());
          Func<double, bool> isInPriceRange = p => p.Between(priceExtream - fractalRange, priceExtream + fractalRange);

          FractalBuyInRange = price.Bid.Between(priceExtream, priceExtream + fractalRange);
          fractalBuy = FractalSellColor = fractalBuy && FractalBuyInRange;

          FractalSellInRange = price.Ask.Between(priceExtream - fractalRange, priceExtream);
          fractalSell = FractalBuyColor = fractalSell && FractalSellInRange;

          PricePosition = priceExtream > Fractals[0].PriceLow
            ? (priceExtream - price.Average) / (priceExtream - Fractals[0].PriceLow)
            : (price.Average - priceExtream) / (Fractals[0].PriceLow - priceExtream);

          IsPriceInPosition = PricePosition <= ui.peakTradeMargin;
          //if (posBS >= posBSMin && isFractalTradeTimeFrame && IsPowerDown) {
          if (isPosBSOk && IsPowerDown && IsPriceInPosition) {
            goBuy = !isInSell;
            goSell = isInSell;
          }
          ti.DencityRatio = densityFoos[tr.densityFoo](goBuy);
          ti.DencityRatioBuy = densityFoos[tr.densityFoo](true);
          ti.DencityRatioSell = densityFoos[tr.densityFoo](false);
          ti.FractalDatesBuy = Fractals.Where(f => f.HasFractalBuy).Select(f => f.StartDate).ToArray();
          ti.FractalDatesSell = Fractals.Where(f => f.HasFractalSell).Select(f => f.StartDate).ToArray();

          if (tr.setLimitOrder) {
            ti.DoTakeProfitBuy = goBuy;
            ti.DoTakeProfitSell = goSell;
          }
        };
        #endregion

        #region Run Decider
        switch (tr.DecisionFoo) {
          case 11: decideByVoltage_11(); break;
          default: throw new InvalidOperationException("Unknown Foo Number:" + tr.DecisionFoo);
        }
        ti.ServerTime = ServerTime;
        var ticksForDelay = ticksInTimeFrame.Skip(ticksInTimeFrame.Length - 100).ToArray();
        ti.TradeSignalDelay = Math.Ceiling(((ticksForDelay.Last().StartDate - ticksForDelay.First().StartDate).TotalSeconds / 100)).ToInt() * 2;
        //if (goBuy && !ti.GoSell) ti.GoBuy = true;// ti.GoBuyTime = ServerTime.AddSeconds(isDirectionOk(true) ? 0 : 1);
        //if (goSell && !ti.GoBuy) ti.GoSell = true;// ti.GoSellTime = ServerTime.AddSeconds(isDirectionOk(false) ? 0 : 1);
        ti.GoBuy = goBuy && !goSell;
        ti.GoSell = goSell && !goBuy;
        if (goSell || goBuy) ti.TradeWaveInMinutes = TimeframeInMinutes;
        #endregion

        #region Corridor
        //var corridorMinimum = SpreadByBarPeriod(tr.corridorMinites, false);
        if (false && CorridorSpread(tr.doTrend) < CorridorSpreadMinimum) {
          ti.CorridorOK = false;
          ti.GoBuy = ti.GoSell = false;
        } else ti.CorridorOK = true;
        #endregion

        VLog = timeSpan("**End**");
      } catch (Exception exc) {
        Log = exc;
      } finally {
        RaisePropertyChanged(() => FractalSellInRange, () => FractalBuyInRange
          , () => IsPowerDown, () => PricePosition, () => IsPriceInPosition);
      }

    }
    void RidOfOldPositions(TradeRequest tr, TradeResponse ti) {
      if (Ticks.Count == 0 || CorridorSpreadInPips == 0) return;
      Func<Trade[], int> maxLots = trades => trades.Max(t => t.Lots) / trades.Min(t => t.Lots);
      #region lotToTrade Functions
      var lotsToTrade_0 = new Func<bool, int>((buy) =>
         Math.Max(1, (int)(buy ? tr.tradesBuy : tr.tradesSell).Length)
      );
      var lotsToTrade_1 = new Func<bool, int>((buy) => {
        var pos = (int)(buy ? tr.tradesBuy : tr.tradesSell).Length;
        return pos * (pos + 1) / 2 + 1;
      });
      var lotsToTrade_2 = new Func<bool, int>((buy) => {
        var pos = (int)(buy ? tr.tradesBuy : tr.tradesSell).Length + 1;
        return pos * (pos + 1) / 2;
      });
      var lotsToTrade_3 = new Func<bool, int>(buy => 1);
      var lotsToTrade_4 = new Func<bool, int>(buy => (buy ? tr.tradesBuy : tr.tradesSell).Length + 1);
      var lotsToTradeFoos = new[] { lotsToTrade_0, lotsToTrade_1, lotsToTrade_2, lotsToTrade_3, lotsToTrade_4 };
      #endregion
      ti.LotsToTradeBuy = lotsToTradeFoos[tr.lotsToTradeFooBuy](true);
      ti.LotsToTradeSell = lotsToTradeFoos[tr.lotsToTradeFooSell](false);
      if (tr.closeAllOnTrade) 
      {
        if (ti.LotsToTradeSell <= 1 && tr.tradesSell.Length > 0) {
          var hasProfit = true || tr.closeOnNet ? tr.SellNetPLPip > 0 : tr.tradesSell.Count(t => t.PL > 0) > 0;
          if (!hasProfit)
            ti.LotsToTradeBuy = tr.tradesSell.Max(t => t.Lots) * 2;
        }
        if (ti.LotsToTradeBuy <= 1 && tr.tradesBuy.Length > 0) {
          var hasProfit = true || tr.closeOnNet ? tr.BuyNetPLPip > 0 : tr.tradesBuy.Count(t => t.PL > 0) > 0;
          if (!hasProfit)
            ti.LotsToTradeSell = tr.tradesBuy.Max(t => t.Lots) * 2;
        }
      }

      #region Rid of Old Positions
      bool doShortStack = Math.Min(tr.SellPositions, tr.BuyPositions) > tr.shortStack;
      bool doTrancate = Math.Min(tr.SellPositions, tr.BuyPositions) >= tr.shortStack + tr.shortStackTruncateOffset; ;
      List<string> closeBuyIDs = new List<string>();
      List<string> closeSellIDs = new List<string>();
      Func<O2G.Trade, bool> fooCloseTrade = t => t.GrossPL > 0 || !tr.closeOnProfitOnly;
      Func<O2G.Trade, bool> closeByWaveFilter = t =>
        Lib.FibRatioSign(t.Remark.TradeWaveInMinutes, tr.tradeAdded.Remark.TradeWaveInMinutes) < tr.closeTradeFibRatio;
      Func<Trade[], IEnumerable<string>> closeByWave = ts =>
        ts.OrderByDescending(t => t.Time).Where(t => t.Time < ServerTime.AddSeconds(-15))
        .Where(closeByWaveFilter).OrderBy(t => t.Remark.TradeWaveInMinutes).Take(1).Select(t => t.Id);
      Func<Trade[], int, double> profitMin = (trades, count) => {
        if (trades.Length < 2) return 0;
        var lastTwo = trades.OrderByDescending(t => t.PL).Take(count).ToArray();
        return Math.Abs(lastTwo.Max(t => t.PL) - lastTwo.Min(t => t.PL)) * tr.profitMin / 10;
      };

      #region Trade Added
      if (tr.tradeAdded != null) {
        var leaveOpenAfterTruncate = 0;
        while (tr.tradeAdded.Buy && tr.SellPositions > 0) {
          if (doShortStack && tr.closeAllOnTrade) {
            closeSellIDs.AddRange(tr.tradesSell.Select(t => t.Id));
            break;
          }
          var plMin = profitMin(tr.tradesSell, 3);
          if (tr.SellNetPLPip >= Math.Abs(plMin)) {
            closeSellIDs.AddRange(tr.tradesSell.Select(t => t.Id));
            break;
          }
          closeSellIDs.AddRange(tr.tradesSell.Where(t => t.PL >= plMin).Select(t => t.Id));
          if (closeSellIDs.Count() > 0) break;
          if (tr.closeOnProfitOnly) closeSellIDs.AddRange(closeByWave(tr.tradesSell));
          else {
            if (doShortStack || tr.BuyPositions > tr.SellPositions - tr.closeOppositeOffset)
              closeSellIDs.Add(tr.tradesSell.Where(fooCloseTrade).OrderBy(t => t.Time).FirstOrLast(tr.sellOnProfitLast).Id);
            if (doTrancate && (tr.SellPositions - tr.BuyPositions).Between(0, 1)) {
              closeSellIDs.AddRange(tr.tradesSell.Select(t => t.Id));
              closeBuyIDs.AddRange(tr.tradesBuy.OrderByDescending(t => t.Time).Skip(leaveOpenAfterTruncate).Select(t => t.Id));
            }
          }
          break;
        }
        while (!tr.tradeAdded.Buy && tr.BuyPositions > 0) {
          if (doShortStack && tr.closeAllOnTrade) {
            closeBuyIDs.AddRange(tr.tradesBuy.Select(t => t.Id));
            break;
          }
          var plMin = profitMin(tr.tradesBuy, 3);
          if (tr.BuyNetPLPip >= Math.Abs(plMin)) {
            closeBuyIDs.AddRange(tr.tradesBuy.Select(t => t.Id));
            break;
          }
          closeBuyIDs.AddRange(tr.tradesBuy.Where(t => t.PL >= plMin).Select(t => t.Id));
          if (closeBuyIDs.Count() > 0) break;
          if (tr.closeOnProfitOnly) closeBuyIDs.AddRange(closeByWave(tr.tradesBuy));
          else {
            if (doShortStack || tr.SellPositions > tr.BuyPositions - tr.closeOppositeOffset)
              closeBuyIDs.Add(tr.tradesBuy.Where(fooCloseTrade).OrderBy(t => t.Time).FirstOrLast(tr.sellOnProfitLast).Id);
            if (doTrancate && (tr.BuyPositions - tr.SellPositions).Between(0, 1)) {
              closeBuyIDs.AddRange(tr.tradesBuy.Select(t => t.Id));
              closeSellIDs.AddRange(tr.tradesSell.OrderByDescending(t => t.Time).Skip(leaveOpenAfterTruncate).Select(t => t.Id));
            }
          }
          break;
        }
        tr.tradeAdded = null;
      }
      #endregion

      #region Colose on Net
      if (tr.closeOnNet && IsPowerDown) {
        Func<Trade[],double, double> plNetMin = (trades,leg) => trades.Length == 0 ? 0 : leg / (trades.Sum(t => t.Lots) / trades.Min(t => t.Lots));
        if (tr.tradesBuy.Length > 1) {
          var plNetMinBuy = plNetMin(tr.tradesBuy, legUpAverageInPips);
          if (tr.BuyNetPLPip >= plNetMinBuy) closeBuyIDs.AddRange(tr.tradesBuy.Select(t => t.Id));
        }
        if (tr.tradesSell.Length > 1) {
          var plNetMinSell = plNetMin(tr.tradesSell, legDownAverageInPips);
          if (tr.SellNetPLPip >= plNetMinSell) closeSellIDs.AddRange(tr.tradesSell.Select(t => t.Id));
        }
      }
      #endregion

      #region Colose on Corridor
      if (tr.closeOnCorridorBorder.HasValue && IsPowerDown && priceCurrent != null && legDownAverageInPips > 0 && legUpAverageInPips > 0) {
        if (tr.closeOnCorridorBorder.Value) {
          Func<Rate, double> priceFunc = rate => rate.PriceAvg;
          Func<Trade, Rate[]> ticksLamda = trade => TicksInTimeFrame.Where(t => t.StartDate >= trade.Time.AddMinutes(-CorridorHeightMinutesBySchedule)).ToArray();
          Func<Trade, double> ticksSpreadInPips = trade => { var ticks = ticksLamda(trade); return fw.InPips(ticks.Max(priceFunc) - ticks.Min(priceFunc)); };
          closeBuyIDs.AddRange(
            tr.tradesBuy.Where(
            t => t.PL >= legUpAverageInPips / 10 && ticksSpreadInPips(t) >= legUpAverageInPips
            //t => t.PL >= (CorridorSpreadInPips / 2) && (priceCurrent.Average - ticksLamda(t).Min(tick => tick.PriceAvg)) >= CorridorSpread(true)
            ).Select(t => t.Id));
          closeSellIDs.AddRange(
            tr.tradesSell.Where(
            t => t.PL >= legDownAverageInPips / 10 && ticksSpreadInPips(t) >= legDownAverageInPips
            //t => t.PL >= (CorridorSpreadInPips / 2) && (-priceCurrent.Average + ticksLamda(t).Max(tick => tick.PriceAvg)) >= CorridorSpread(true)
            ).Select(t => t.Id));
        } else {
          closeBuyIDs.AddRange(tr.tradesBuy.Where(t => t.PL > legUpAverageInPips/* t.Remark.TradeWaveHeight*/).Select(t => t.Id));
          closeSellIDs.AddRange(tr.tradesSell.Where(t => t.PL > legDownAverageInPips/* t.Remark.TradeWaveHeight*/).Select(t => t.Id));
        }
      }
      #endregion

      if (tr.rsiProfit > 0) {
        if (ti.RsiHigh >= tr.rsiTresholdSell)
          closeBuyIDs.AddRange(tr.tradesBuy.Where(t => t.PL >= tr.rsiProfit).Select(t => t.Id));
        if (ti.RsiLow <= tr.rsiTresholdBuy)
          closeSellIDs.AddRange(tr.tradesSell.Where(t => t.PL >= tr.rsiProfit).Select(t => t.Id));
      }

      if (tr.closeIfProfitTradesMoreThen > 0) {
        Func<Trade[], int> tradesCount =
          (trades) => tr.closeProfitTradesMaximum > 0 ? tr.closeProfitTradesMaximum : trades.Length + tr.closeProfitTradesMaximum;
        {
          var plMin = Math.Ceiling(profitMin(tr.tradesBuy, 2));
          var closeBuys = tr.tradesBuy.Length > 1 ? tr.tradesBuy.Where(t => t.PL >= plMin).ToArray() : new Trade[] { };
          if (closeBuys.Length >= Math.Min(tr.tradesBuy.Length, tr.closeIfProfitTradesMoreThen + 1)) {
            var tc = tradesCount(closeBuys);
            closeBuyIDs.AddRange(closeBuys.OrderByDescending(t => t.GrossPL).Take(tc).Select(t => t.Id));
          }
        }
        {
          var plMin = Math.Ceiling(profitMin(tr.tradesSell, 2));
          var closeSells = tr.tradesSell.Length > 1 ? tr.tradesSell.Where(t => t.PL >= plMin).ToArray() : new Trade[] { };
          if (closeSells.Length >= Math.Min(tr.tradesSell.Length, tr.closeIfProfitTradesMoreThen + 1)) {
            var tc = tradesCount(closeSells);
            closeSellIDs.AddRange(closeSells.OrderByDescending(t => t.GrossPL).Take(tc).Select(t => t.Id));
          }
        }
      }
      if (closeBuyIDs.Count > 0 && tr.BuyNetPLPip > 0)
        closeBuyIDs.AddRange(tr.tradesBuy.Select(t => t.Id));
      if (closeSellIDs.Count > 0 && tr.SellNetPLPip > 0)
        closeSellIDs.AddRange(tr.tradesSell.Select(t => t.Id));
      ti.CloseTradeIDs = closeBuyIDs.Union(closeSellIDs).Distinct().ToArray();
      if (ti.CloseTradeIDs.Length > 0) {
        var i = 0;
      }
      #endregion
    }
    #endregion

    #region FX Event Handlers
    void fxCoreWrapper_EURJPY_PriceChanged(Order2GoAddIn.Price price) {
      Price = price;
      ProcessPrice(price, ref _ticks);
    }
    void fxCoreWrapper_EURJPY_RowRemoving(string TableType, string RowID) {
      Log = "Row " + RowID + " is being removed.";
    }
    void fxCoreWrapper_EURJPY_OrderAdded(FXCore.RowAut fxRow) {
      Log = "Order " + fxRow.CellValue("OrderID") + "";
    }
    void coreFX_LoginError(Exception exc) {
      Log = exc;
    }
    void coreFX_LoggedInEvent(object sender, EventArgs e) {
      Log = "User " + ui.Account + " logged in.";
      CorridorsWindow_EURJPY.Show();
      Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => {
        fxCoreWrapper_EURJPY_PriceChanged(null);
      }));
    }
    #endregion

    #region Login
    private void Login(object sender, RoutedEventArgs e) {
      Log = "Login is in progress ...";
      Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => {
        coreFX.LogOn(ui.Account, txtPassword.Text, ui.isDemo);
        if (VolatilityScheduler == null) {
          VolatilityScheduler = new ThreadScheduler(TimeSpan.FromMilliseconds(0), TimeSpan.FromMinutes(1), () => { GetVolatility(); }, (s, ee) => Log = ee.Exception);
          VolatilityScheduler.Finished += (s1, e1) => {
            if (getTicksCommand != null) {
              TicksScheduler.Command = getTicksCommand;
              getTicksCommand = null;
            }
          };

        }
      }));
    }
    #endregion

    #region INotifyPropertyChanged Members
    public event PropertyChangedEventHandler PropertyChanged;
    void RaisePropertyChanged(params Expression<Func<object>>[] propertyLamdas) {
      if (propertyLamdas == null || propertyLamdas.Length == 0) RaisePropertyChangedCore();
      else
        foreach (var pl in propertyLamdas) {
          RaisePropertyChanged(pl);
        }
    }
    void RaisePropertyChanged(Expression<Func<object>> propertyLamda) {
      var body = propertyLamda.Body as UnaryExpression;
      if (body == null) {
        PropertyChanged.Raise(propertyLamda);
      } else {
        var operand = body.Operand as MemberExpression;
        var member = operand.Member;
        RaisePropertyChangedCore(member.Name);
      }
    }
    void RaisePropertyChangedCore(params string[] propertyNames) {
      if (PropertyChanged == null) return;
      if (propertyNames.Length == 0)
        propertyNames = new[] { new System.Diagnostics.StackFrame(1).GetMethod().Name.Substring(4) };
      foreach (var pn in propertyNames)
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => {
          PropertyChanged(this, new PropertyChangedEventArgs(pn));
        }));
    }
    #endregion

    #region Main Window Event Handlers
    private void TextChanged_TimeFrameStart(object sender, TextChangedEventArgs e) {
      TextChanged(sender, e);
      if (TestMode || (sender as TextBox).Text.Length == 0) {
        GetTicksAsync();
      } else
        GetTicksAsync();
    }
    private void TextChanged_ResetTicks(object sender, TextChangedEventArgs e) {
      _ticks.Clear();
      if (sender != null) TextChanged(sender, e);
    }
    private void TextChanged(object sender, TextChangedEventArgs e) {
      var tb = (sender as TextBox);
      var name = tb.Name;
      this.ui.SetProperty("_" + name, tb.Text);
      priceHeightMinOld = priceHeightMaxOld = 0;
    }
    void ServerWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
      fw.Dispose();
    }
    private void Checked(object sender, RoutedEventArgs e) {
      var chb = (sender as CheckBox);
      var name = chb.Name;
      this.ui.SetProperty("_" + name, chb.IsChecked);
    }
    private void HighMinutes_TextChanged(object sender, TextChangedEventArgs e) {
      RatesHigh.Clear();
      TextChanged(sender, e);
    }
    #endregion

    static bool HasSeconds(string text) { return Regex.IsMatch(text, @"\d\d:\d\d:\d\d"); }
    private void TimeFrameUp_Click(object sender, RoutedEventArgs e) {
      if (!HasSeconds(txtTimeFrameTimeStart.Text))
        txtTimeFrameTimeStart.Text = ui.timeFrameTimeStart.AddMinutes(1).ToString("MM/dd/yy HH:mm");
      else
        txtTimeFrameTimeStart.Text = ui.timeFrameTimeStart.AddSeconds(1).ToString("MM/dd/yy HH:mm:ss");
      TextChanged(txtTimeFrameTimeStart, null);
    }

    private void TimeFrameDown_Click(object sender, RoutedEventArgs e) {
      if (!HasSeconds(txtTimeFrameTimeStart.Text))
        txtTimeFrameTimeStart.Text = ui.timeFrameTimeStart.AddMinutes(-1).ToString("MM/dd/yy HH:mm");
      else
        txtTimeFrameTimeStart.Text = ui.timeFrameTimeStart.AddSeconds(-1).ToString("MM/dd/yy HH:mm:ss");
      TextChanged(txtTimeFrameTimeStart, null);
    }

    private void chkCorridorByMinimumVolatility_Loaded(object sender, RoutedEventArgs e) {
      DependencyPropertyDescriptor.FromProperty(CheckBox.IsCheckedProperty, this.GetType()).AddValueChanged(chkCorridorByMinimumVolatility, (s, re) => {
        ui.corridorByMinimumVolatility = chkCorridorByMinimumVolatility.IsChecked;
      });
    }

    private void chkMassOrHeight_Loaded(object sender, RoutedEventArgs e) {
      DependencyPropertyDescriptor.FromProperty(CheckBox.IsCheckedProperty, this.GetType()).AddValueChanged(chkMassOrHeight, (s, re) => {
        ui.massOrHeight = chkMassOrHeight.IsChecked;
      });
    }


    #region IDisposable Members
    ~ServerWindow() {
      Dispose(false);
    }
    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }
    void Dispose(bool disposing) {
      if (disposing) {
      }
      fw.Dispose();
    }

    #endregion
  }
  public static class PropertyChangedExtensions {
    public static void Raise(this PropertyChangedEventHandler handler, LambdaExpression propertyExpression) {
      if (handler != null) {
        // Retreive lambda body
        var body = propertyExpression.Body as MemberExpression;
        if (body == null)
          throw new ArgumentException("'propertyExpression' should be a member expression");

        // Extract the right part (after "=>")
        var vmExpression = body.Expression as ConstantExpression;
        if (vmExpression == null)
          throw new ArgumentException("'propertyExpression' body should be a constant expression");

        // Create a reference to the calling object to pass it as the sender
        LambdaExpression vmlambda = System.Linq.Expressions.Expression.Lambda(vmExpression);
        Delegate vmFunc = vmlambda.Compile();
        object vm = vmFunc.DynamicInvoke();

        // Extract the name of the property to raise a change on
        string propertyName = body.Member.Name;
        var e = new PropertyChangedEventArgs(propertyName);
        handler(vm, e);
      }
    }
  }
}
