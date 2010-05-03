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
using HedgeHog.Rsi;
using HedgeHog.Models;

namespace HedgeHog {
  public sealed partial class ServerWindow : WindowModel, IServer, IDisposable {
    #region Log
    object Log {
      set {
        if ((value + "").Length == 0) return;
        var exc = value as Exception;
        var message = exc == null ? value + "" : exc.Message;
        txtLog.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(delegate() {
          var lines = txtLog.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).ToList();
          var time = DateTime.Now.ToString("[HH:mm:ss] ");
          lines.Insert(0,time + message);
          txtLog.Text = string.Join(Environment.NewLine, lines.Skip(lines.Count - 30).ToArray());
          //txtLog.ScrollToEnd();
          var text = DateTime.Now.ToString("[dd HH:mm:ss] ") + message + Environment.NewLine + (exc == null ? "" : exc.StackTrace + Environment.NewLine);
          while (exc != null && (exc = exc.InnerException) != null)
            text += "**************** Inner ***************" + Environment.NewLine + exc.Message + Environment.NewLine + exc.StackTrace + Environment.NewLine;
          System.IO.File.AppendAllText(logFileName, text);
        })
        );
      }
    }
    object VLog { set { if (ui.verboseLogging)Log = value; } }
    #endregion

    public SimpleDelegateCommand FillRsisCommand { get; set; }
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
    public string Pair { get { return fw == null ? "" : fw.Pair; } }
    List<Rate> _ticks = new List<Rate>();
    List<Rate> Ticks {
      get { return doTicks || lastBar.Count == 0 ? _ticks : _ticks.Concat(new[] { lastBar }).ToList(); }
      set { _ticks = value; }
    }
    List<Volt> Voltages = new List<Volt>();
    List<Rate> RatesHigh = new List<Rate>();
    Corridors CorridorsWindow_EURJPY;
    ThreadScheduler RsiTuneUpScheduler;
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
    public int TimeframeInMinutes { get { return Timeframe == DateTime.MinValue ? 0 : (fw.ServerTimeCached - Timeframe).TotalMinutes.ToInt(); } }
    DateTime _timeframe = DateTime.MinValue;
    public DateTime Timeframe {
      get { return _timeframe; }
      set {

        if (_timeframe == value) return;
        var minutes = (fw.ServerTimeCached - value).TotalMinutes;
        var minutesAvg = _timeframe == DateTime.MinValue ? 0 : (fw.ServerTimeCached - _timeframe).TotalMinutes;
        _timeframe = fw.ServerTimeCached.AddMinutes(-Lib.CMA(minutesAvg, 0.0, TicksPerMinuteMax, minutes));
        RaisePropertyChanged(() => TimeframeInMinutes);
        RaisePropertyChanged(() => PeakPriceHighAverage);
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

    bool? buySell;
    public bool? BuySell {
      get { return buySell; }
      set { buySell = value; RaisePropertyChangedCore(); }
    }


    bool? _tradeDirection;
    public string TradeDirection { get {
      if (_tradeDirection.HasValue) return _tradeDirection.Value ? "↑" : "↓";
      return Fractals.Length < 2 ? "" : isInBuy ? "↑" : "↓";
    }
      set { _tradeDirection = bool.Parse(value); }
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

    int _ticksPerMinuteMax;
    public int TicksPerMinuteMax {
      get { return _ticksPerMinuteMax; }
      set { _ticksPerMinuteMax = value; RaisePropertyChangedCore(); }
    }

    int _ticksPerMinuteCurr;
    public int TicksPerMinuteCurr {
      get { return _ticksPerMinuteCurr; }
      set { _ticksPerMinuteCurr = value; RaisePropertyChanged(); }
    }
    int _ticksPerMinutePrev;
    public int TicksPerMinutePrev {
      get { return _ticksPerMinutePrev; }
      set { _ticksPerMinutePrev = value; RaisePropertyChanged(); }
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
      var spreadShort = (_ticksInTimeFrame.Length > 0 ? _ticksInTimeFrame : TicksInTimeFrame)
        .ToArray().GetMinuteTicks(period).Average(spreadLambda);
      if (shortOnly || RatesHigh.Count == 0) return spreadShort;
      var spreadLong = fw.GetMinuteBars(RatesHigh.ToArray(), period).Average(spreadLambda);
      return Math.Max(spreadShort, spreadLong);
    }

    double? _a;
    double A {
      get { return _a.GetValueOrDefault(); }
      set { _a = double.IsNaN(value) ? 0 : value; RaisePropertyChanged(() => AngleRounded, () => AngleColor); }
    }

    double? _a1;
    double A1 {
      get { return _a1.GetValueOrDefault(); }
      set { _a1 = double.IsNaN(value) ? 0 : value; RaisePropertyChanged(() => Angle1Rounded, () => Angle1Color); }
    }

    bool AreAnglesBuy { get { return A1 >= 0; } }
    bool AreAnglesSell { get { return A1 <= 0; } }

    public double Angle1 { get { return Math.Atan(A1) * (180 / Math.PI); } }
    public double Angle { get { return Math.Atan(A) * (180 / Math.PI); } }
    
    public double AngleRounded { get { return Math.Round(Angle / fw.PointSize, 2); } }
    public double Angle1Rounded { get { return Math.Round(Angle1 / fw.PointSize, 2); } }

    public bool? AngleColor { get { return A == 0 ? (bool?)null : A > 0; } }
    public bool? Angle1Color { get { return A1 == 0 ? (bool?)null : A1 > 0; } }

    double[] _regressionCoeffs = new double[] { };

    double[] RegressionCoefficients {
      get { return _regressionCoeffs; }
      set {
        _regressionCoeffs = value;
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

    Rate _rsiLocalMin;
    public Rate RsiLocalMin {
      get { return _rsiLocalMin; }
      set { _rsiLocalMin = value; RaisePropertyChangedCore(); }
    }

    RsiStatistics _rsiStats;

    public RsiStatistics RsiStats {
      get { return _rsiStats; }
      set { _rsiStats = value; RaisePropertyChangedCore(); }
    }

    bool isRsiSell;
    public bool IsRsiSell {
      get { return isRsiSell; }
      set { isRsiSell = value; RaisePropertyChangedCore(); }
    }

    bool isRsiBuy;
    public bool IsRsiBuy {
      get { return isRsiBuy; }
      set { isRsiBuy = value; RaisePropertyChangedCore(); }
    }


    Rate _rsiLocalMax;
    public Rate RsiLocalMax {
      get { return _rsiLocalMax; }
      set { _rsiLocalMax = value; RaisePropertyChangedCore(); }
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
    public ServerWindow():this("") { }
    public ServerWindow(string name) {
      this.Name = name;
      FillRsisCommand = new SimpleDelegateCommand(o =>{
        FillRsis(true);
      });
      FillRsisCommand.GestureKey = Key.Enter;
      fw = new Order2GoAddIn.FXCoreWrapper(coreFX);
      InitializeComponent();
      System.IO.File.Delete(logFileName);
      Closing += new System.ComponentModel.CancelEventHandler(Window_Closing);
      coreFX.LoggedInEvent += new EventHandler<EventArgs>(coreFX_LoggedInEvent);
      coreFX.LoginError += new Order2GoAddIn.CoreFX.LoginErrorHandler(coreFX_LoginError);
      fw.Pair = cmbPair.Text;
      fw.OrderAdded += new Order2GoAddIn.FXCoreWrapper.OrderAddedEventHandler(fxCoreWrapper_EURJPY_OrderAdded);
      fw.RowRemoving += new Order2GoAddIn.FXCoreWrapper.RowRemovingdEventHandler(fxCoreWrapper_EURJPY_RowRemoving);
      fw.PriceChanged += new Order2GoAddIn.FXCoreWrapper.PriceChangedEventHandler(fxCoreWrapper_EURJPY_PriceChanged);
      CorridorsWindow_EURJPY = new Corridors(fw.Pair);
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
      RsiTuneUpScheduler = new ThreadScheduler((s, e) => Log = e.Exception);
      PositionHelper = new CloseByPositionHelper(fw);
      if (coreFX.IsLoggedIn)
        Login(null, null);
    }
    #endregion
    //Dimok Trade Signals by Currency(not pair)
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
        if (ratePeriod == 0 && _ticks.Count == 0 /*|| fractalChanged*/) {
          fractalChanged = false;
          Ticks = fw.GetTicks(ui.ticksBack).OfType<Rate>().OrderBars().ToList();
          Ticks.AddUp(fw.GetTicks(550));
          FillRsis();
        }
        //var ticks = _ticks.ToArray().TakeWhile(b => b.IsHistory).ToArray();
        //if ((_ticks.Last().StartDate - ticks.Last().StartDate).Duration().TotalSeconds > 30) {
        //  //fw.GetBars(ratePeriod, StartDate, EndDate, ref ticks);
          var ts = _ticks.ToArray().AddUp(fw.GetTicks(300)).ToList();
          lock (_ticks) {
            Ticks = ts;
          //}
          ////Ticks.FillMass();
        }
        //TicksPerMinute(Ticks);
        FillRsis();
        RaisePropertyChanged(() => TimeframeByTicksMin);
        //_ticks.ToArray().FillPower(TimeSpan.FromMinutes(1));
        RunMinutesBack(null);
      } catch (Exception exc) {
        Log = exc;
      }
      //Select((b, i) => new Tick() {
      //  Ask = b.Ask, Bid = b.Bid, StartDate = b.StartDate, Row = i + 1, IsHistory = b.IsHistory
      //}).ToList();
    }


    double? _rsiTicksAverage;
    public int RsiTicksAverage {
      get { return _rsiTicksAverage.GetValueOrDefault().ToInt(); }
      set {
        _rsiTicksAverage = _rsiTicksAverage.Cma(10, value);
        RaisePropertyChangedCore(); }
    }


    double? _rsiTicks;
    public int rsiTicks {
      get { return _rsiTicks.GetValueOrDefault().ToInt(); }
      set {
        if (_rsiTicks.HasValue && ((value - _rsiTicks.GetValueOrDefault()) / _rsiTicks).Abs() < .01) return;
        _rsiTicks = _rsiTicks.Cma(3, value);
        Dispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => {
          txtRsiTicks.Text = rsiTicks + "";
        }));
      }
    }
    private void FillRsis() { FillRsis(false); }
    private void FillRsis(bool Refresh) {
      rsiTicks = (_ticks.Count * ui.RsiPeriodRatio).ToInt();
      _ticks.ToArray().Rsi(rsiTicks, Refresh);
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
          (doTicks ? ui.groupTicks ? Ticks.GroupTicksToRates() : Ticks : Ticks).ToArray();
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
    Rate[] rsiFractals = new Rate[] { };

    static string Format1(double value,double minValue,string formatMin,string formatOther){
      return value.ToString(Math.Abs(value) < minValue ? formatMin : formatOther);
    }
    public string FractalWavesText {
      get {
        try {
          double tpm;
          return string.Join(Environment.NewLine, Fractals1.Take(5).Concat(rsiFractals).
            Select((f, i) => f.StartDate.ToString("HH:mm:ss") + " " + (f.Fractal == FractalType.Buy ? "B" : f.Fractal == FractalType.Sell ? "S" : "N")
              + (i < Fractals1.Length - 1
              ? "|" + fw.InPips(f.Ph.Height.Abs(), 0).ToInt().ToString("00")
              //+ "|" + (tpm = _ticks.TradesPerMinute(f, Fractals1[i + 1])).ToString("00")
              //+ "|" + string.Format("{0:0}K", fw.InPips(1, f.Ph.MassByTradesPerMinute.GetValueOrDefault() / 1000))
              ////+ "|" + Format1(fw.InPips(2, f.Ph.Power.Value, 1), 10, " 0.0;-0.0", " 00;-00")
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
    public bool TradeByShortOverlapOk { get { return OverlapLast.TotalSeconds > OverlapAverageShort.TotalSeconds.ToInt(); } }
    private bool tradeByOverlap { get { return false && OverlapLast.TotalMinutes > Math.Ceiling(OverlapAverageShort.TotalMinutes); } }
    public bool TradeByOverlapBuy { get { return !isInSell && tradeByOverlap; } }
    public bool TradeByOverlapSell { get { return isInSell && tradeByOverlap; } }
    public string FractalStats { get; set; }
    DateTime fractalsLastUpdate;
    public bool IsPowerDown { get; set; }
    double legUpAverageInPips;
    double legDownAverageInPips;
    double baseHeight;
    double baseHeightInPips { get { return fw.InPips(baseHeight); } }
    bool isInSell { get { return Fractals[0].HasFractalBuy; } }
    bool isInBuy { get { return !isInSell; } }
    double posBS;
    bool isLegOk, isPosBSOk;
    ComboBoxItem _positionModeItem;

    public ComboBoxItem PositionModeItem {
      get { return _positionModeItem; }
      set { 
        _positionModeItem = value;
        PositionMode = (PositionModeType)(value.Parent as ComboBox).SelectedIndex;
      }
    }

    enum PositionModeType { Mass = 0, Height = 1, MassOrHeight = 2, MassByHeight = 3, MassByHeightOrMassOrHigh = 4 }

    PositionModeType _positionMode;

    private PositionModeType PositionMode {
      get { return _positionMode; }
      set { _positionMode = value; }
    }

    double PowerInPips(double power, int roundTo) { return fw.InPips(2, power, roundTo); }
    private static void DebugWriteLineStopWatch(string line, Stopwatch sw) {
      System.Diagnostics.Debug.WriteLine(line + sw.ElapsedMilliseconds + "ms.");
      sw.Reset(); sw.Start();
    }
    public delegate double FuncBase(Func<BarBase, double?> value);
    public delegate double FuncBase1(IEnumerable<BarBase> bars, Func<BarBase, double?> value);
    FuncBase1 Average3 = (fractals, f) => {
      return fractals.Take(2).TakeWhile(t => f(t).HasValue).Select(f).Average().Value;
    };
    FuncBase1 Average4 = (fractals, f) => {
      var fs = fractals.Take(3).TakeWhile(t => f(t).HasValue).Select(f).ToArray();
      return fs.Length == 0 ? 0 : new[] { fs.First(), fs.Last() }.Average().Value;
    };
    class FractalHistory {
      public Rate Fractal { get; set; }
      public int Count { get; set; }
      public FractalHistory() {

      }
    }
    Dictionary<DateTime, int> BannedFractals = new Dictionary<DateTime, int>();

    bool canTrade;

    public bool CanTrade {
      get { return canTrade; }
      set { canTrade = value; RaisePropertyChangedCore(); }
    }

    TimeSpan _fractalsInterval;
    public TimeSpan FractalsInterval {
      get { return _fractalsInterval; }
      set { _fractalsInterval = value; RaisePropertyChangedCore(); }
    }

    DateTime tradesPerMinuteTime = DateTime.MinValue;
    public CloseByPositionHelper PositionHelper { get; set; }
    private void GetPositionByFractals() {
      FuncBase bsBaseFoo1 = (f) => Fractals.Take(4).OrderBy(f).Skip(1).OrderByDescending(f).Skip(1).Select(f).Average().Value;
      FuncBase bsBaseFoo2 = (f) => Fractals.Take(3).Average(f).Value;
      FuncBase bsBaseFoo3 = (f) => Fractals.Take(3).Select(f).OrderByDescending(m => m).Take(2).Average().Value;
      FuncBase bsBaseFoo4 = (f) => Average3(Fractals, f);
        var bsBaseFoo = bsBaseFoo4;
        try {
          var ret = new List<string>();
          #region TickByMinute
          var ticksInTimeFrame = TicksInTimeFrame;
          var ticksArray = Ticks.ToArray();
          var ticksByMinute = ticksArray.GetMinuteTicks(1).OrderBarsDescending().ToArray();
          var dateLast = ServerTime.AddSeconds(-30);
          var ticksByMinute1 = ticksArray.Where(t => t.StartDate < dateLast).ToArray().GetMinuteTicks(1).OrderBarsDescending().ToArray();
          if ((DateTime.Now - tradesPerMinuteTime) > TimeSpan.FromMinutes(1)) {
            tradesPerMinuteTime = DateTime.Now;
            TicksPerMinuteAverageLong = (Fractals.Length > 1 ? ticksArray.TradesPerMinute(Fractals.Take(4).Last(), Fractals[0]) : ticksArray.TradesPerMinute()).ToInt();
          }
          TicksPerMinuteCurr = (Fractals1.Length < 2 ? TicksPerMinuteAverageLong : ticksArray.TradesPerMinute(Fractals1[0], Fractals1[1])).ToInt();
          TicksPerMinutePrev = (Fractals1.Length < 4 ? TicksPerMinuteCurr : ticksArray.TradesPerMinute(Fractals1[2], Fractals1[3])).ToInt();
          var cmaPeriod = TicksPerMinuteCurr / 2.0;
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
          OverlapAverageShort = Fractals.Length == 0 ? OverlapAverage :
            TimeSpan.FromSeconds(Lib.CMA(OverlapAverageShort.TotalSeconds, 0, cmaPeriod,
            ticksByMinute.Where(t => t.StartDate >= Fractals[0].StartDate).Average(r => r.Overlap.TotalSeconds))
          );

          if (false) {
            ticksArray.ToArray().FillFlatness(5);
            OverlapLast = ticksArray.Last().Flatness.Value;
            {
              var l = new List<TimeSpan>();
              if (ui.useOverlapLast) l.Add(OverlapLast);
              if (ui.useOverlapShort) l.Add(OverlapAverageShort);
              var tp = new[] { TimeSpan.FromMinutes(1), l.Count == 0 ? new[] { OverlapLast, OverlapAverageShort }.Max() : l.Average() }.Max();
              var ticksToRegress = ticksArray.Where(t => t.StartDate >= _serverTimeCached - tp).ToArray();
              var regress = Lib.Regress(ticksToRegress.Select(t => t.PriceAvg).ToArray(), 1);
              A = regress[1];
            }
            if (OverlapLast.TotalSeconds == 0) return;
          } else {
            var ticksToRegress = ticksArray.Where(t => t.StartDate >= _serverTimeCached - OverlapAverageShort).ToArray();
            var regress = Lib.Regress(ticksToRegress.Select(t => t.PriceAvg).ToArray(), 1);
            A = regress[1];
          }
          {
            var tl = ticksArray.Last();
            var ticksPerLastMinute = ticksArray.Where(TimeSpan.FromMinutes(1), tl).Count();
            TicksPerMinuteMax = new[] { TicksPerMinuteCurr, TicksPerMinutePrev, TicksPerMinuteAverageLong, ticksPerLastMinute }.Max();
            var regress = Lib.Regress(ticksArray.Skip(ticksArray.Length - TicksPerMinuteMax).Select(t => t.PriceAvg).ToArray(), 1);
            A1 = _a1.Cma(1, regress[1]).Value;
          }
          #endregion

          #region Rsi
          var rsiSlack = ticksInTimeFrame.Last().StartDate.Subtract(OverlapAverageShort);
          var rsiTicks = ticksInTimeFrame
            .Where(t => t.StartDate >= rsiSlack)
            //.Skip(ticksInTimeFrame.Length - (ui.rsiTicksDelay))
            .Where(hasRsi).OrderBy(t => t.PriceRsi).ToArray();
          if (rsiTicks.Length == 0) {
            CanTrade = false;
            return;
          }
          RsiLocalMin = rsiTicks.First();
          RsiLocalMax = rsiTicks.Last();
          TradeDirection = (new[] { RsiLocalMax.PriceRsi + RsiLocalMin.PriceRsi }.Average() < 50) + "";
          if (ticksInTimeFrame.Last().PriceRsi.HasValue) {
            var rs = ticksInTimeFrame.RsiStats();
            if (rs.Sell > 50.01 && rs.Buy < 49.99) RsiStats = rs;
          } else
            Log = "Last tick - no RSI.";
          rsiFractals = ticksInTimeFrame
            .FindWaves(b => Math.Sign(b.PriceRsi.GetValueOrDefault(50) - 50), b => b.PriceRsi);

          #endregion

          _CorridorHeightMinutesBySchedule = Lib.CMA(_CorridorHeightMinutesBySchedule, cmaPeriod, (TimeSpan.FromSeconds((OverlapAverage.TotalSeconds * 2))).TotalMinutes);


          #region Fractals/fractals
          if (DateTime.Now - fractalsLastUpdate > TimeSpan.FromSeconds(10)) {
            var fractalCountMaximun = 10;
            var fractalTicks = ticksArray.GroupTicksToRates().OrderBarsDescending().ToArray();
            Func<double?, Rate[]> findFractals = wave => fractalTicks.FindFractalTicks(wave.Value, TimeSpan.FromMinutes(CorridorHeightMinutesBySchedule), 1, fractalCountMaximun, new Rate[] { }).OrderBarsDescending().ToArray();
            Fractals = findFractals(0);

            if (false) { ticksArray.FillPower(TimeSpan.FromMinutes(1)); }

            Fractals.FillFractalHeight();

            #region Normalize Fractal
            var legAllAverageStatsLocal = Fractals.Where(f => f.Ph.Height.HasValue).Select(f => f.Ph.Height.Abs().Value).ToArray().GetWaveStats();
            legAverageBottom = Lib.CMA(legAverageBottom, cmaPeriod, legAllAverageStatsLocal.Average - legAllAverageStatsLocal.StDev);
            if (ui.normalizeFractals) {
              var f = findFractals(legAverageBottom.Value);
              if (f.Length >= ui.RsiWavesForCorrelation) {
                Fractals = f;
                Fractals.FillFractalHeight();
              }
            }

            fractalsLastUpdate = DateTime.Now;
          }
          var legAllAverageStats = Fractals.Where(f => f.Ph.Height.HasValue).Select(f => f.Ph.Height.Abs().Value).ToArray().GetWaveStats();
            #endregion

          #region Fractals1
          Fractals1 = Fractals.Concat(new[] { ticksArray.Last().Clone() as Tick }).OrderBarsDescending().ToArray();
          if (Fractals1.Length > 1)
            Fractals1[0].Fractal = Fractals1[1].HasFractalSell ? FractalType.Buy : FractalType.Sell;
          Fractals1.FillFractalHeight();
          if (Fractals.Length > 1) {
            var ticksAfterFractal = ticksArray.Where(t => t.StartDate >= Fractals1[1].StartDate).ToArray();
            Fractals1[0].Ph.Height = ticksAfterFractal.Max(t => t.PriceAvg) - ticksAfterFractal.Min(t => t.PriceAvg);
          }
          #endregion

          if (Fractals.Length > 0 && ui.tradeByFractal) {
            PositionHelper.Fill(priceCurrent, ticksArray, Fractals[1]);
            RaisePropertyChanged(() => PositionHelper, () => TradeDirection);
          }

          #endregion

          CorridorSpreadMinimum = Fractals.Skip(1).Select((f, i) => Math.Abs(f.PriceAvg - Fractals[i].PriceAvg)).Average();


          if (Fractals.Length < 2) {
            ret.Add(Fractals.Length + " < 2");
            FractalStats = string.Join(Environment.NewLine, ret.ToArray());
            RaisePropertyChanged(() => FractalStats);
            return;
          }

          {
            var ff = Fractals1.Where(f => f.Ph.Time.HasValue).Select(f => f.Ph.Time.Value).Take(4).ToArray();
            FractalsInterval = ff.Average();
          }

          //priceCurrent.Spread * 10 < new[] { legUpAverageInPips, legDownAverageInPips }.Min()
          CanTrade = !ui.tradeByFractal || FractalsInterval < TimeSpan.FromMinutes(45);// && OverlapAverageShort.TotalMinutes < 3;

          #region Legs Up/Down
          //var legUps = Fractals1.Where(f => f.HasFractalSell && f.Ph.Height.HasValue).ToArray();
          //legUpAverageInPips = fw.InPips(legUps.Length == 0 ? 0 : legUps.Average(f => f.Ph.Height.Abs()), 0);
          legUpAverageInPips = fw.InPips(Average4(Fractals.SkipWhile(f => f.HasFractalBuy), f => f.Ph.Height.Abs()), 1);
          if (legUpAverageInPips == 0) return;
          //var waveUpsPeriod = legUps.Length == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(legUps.Average(f => (f.f.StartDate - Fractals[f.i + 1].StartDate).TotalSeconds));

          //var legDowns = Fractals1.Where(f => f.HasFractalBuy && f.Ph.Height.HasValue).ToArray();
          //legDownAverageInPips = fw.InPips(legDowns.Length == 0 ? 0 : legDowns.Average(f => f.Ph.Height.Abs()), 0);
          legDownAverageInPips = fw.InPips(Average4(Fractals.SkipWhile(f => f.HasFractalSell), f => f.Ph.Height.Abs()), 1);
          if (legDownAverageInPips == 0) return;
          //var waveDownsPeriod = legDowns.Length == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(legDowns.Average(f => (f.f.StartDate - Fractals[f.i + 1].StartDate).TotalSeconds));
          #endregion

          #region Legs
          //        var fractalsWithHeight = fractals.Where(f => f.h > 0).OrderByDescending(f => f.h).ToArray();
          //        var legAllAverage = fw.InPips(fractalsWithHeight.Skip(fractalsWithHeight.Length > 1 ? 1 : 0).Average(f => f.h), 1);
          var legAllAverage = legAllAverageStats.Average;
          var legTradeAverage = isInSell ? legUpAverageInPips : legDownAverageInPips;
          var fractalTradeLong = Fractals[0];
          var legPriceTicks = ticksByMinute.Where(t => t.StartDate > fractalTradeLong.StartDate);
          var legPrice = isInSell ?
            legPriceTicks.Max(t => t.PriceHigh) - fractalTradeLong.FractalPrice.Value
            : fractalTradeLong.FractalPrice.Value - legPriceTicks.Min(t => t.PriceLow);
          #endregion

          this.baseHeight = bsBaseFoo(f => f.Ph.Height.Abs());
          var heightRatio = Fractals1[0].Ph.Height.Abs() / baseHeight;
          {
            double? posBS = heightRatio;
            var posBSMin = ui.mass1Mass0TradeRatio;
            this.isLegOk = heightRatio >= posBSMin;
            this.posBS = posBS.Value;
            this.isPosBSOk = this.posBS >= posBSMin;
          }

          #region Show Stats
          ret.Add("LegUA : " + legUpAverageInPips.ToString("n1"));
          ret.Add("LegDA : " + legDownAverageInPips.ToString("n1"));
          ret.Add("LegAA : " + string.Format("{0:n1}/{1:n1}/{2:n1}", fw.InPips(legAllAverage), fw.InPips(legAllAverageStats.AverageUp), fw.InPips(legAverageBottom)));
          ret.Add("LegASD: " + fw.InPips(legAllAverageStats.StDev, 1).ToString("n1"));
          ret.Add("CorSM : " + CorridorSpreadMinimumInPips.ToString("n1"));
          ret.Add("FtlTm : " + Fractals.Where(f => f.Ph.Time.HasValue).Average(f => f.Ph.Time.Value.TotalMinutes).ToString("n1"));
          ret.Add("PosHgh: " + string.Format("{0:p1}>{1:n1}", heightRatio, fw.InPips(baseHeight)));
          ret.Add("PosBS: " + string.Format("{0:p1}", this.posBS));
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
        } catch (Exception exc) {
          CanTrade = false;
          Log = exc;
        } finally {
        RaisePropertyChanged(() => CorridorSpreadInPips);
        RaisePropertyChanged(() => FractalWavesText, () => FractalWaveColor
          , () => OverlapLast, () => OverlapLastPerc
          , () => OverlapAverageShortPower, () => FractalStats
          , () => OverlapStDev, () => OverlapAverage, () => OverlapAverageShort);
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
      Timeframe = ui.RsiPeriodRatio > 0
        ? _ticks.Skip((_ticks.Count * ui.RsiPeriodRatio).ToInt()).First().StartDate
        : Fractals.Take(ui.RsiWavesForCorrelation).Min(f => f.StartDate);
      MinutesBackSampleCount = (fw.ServerTimeCached - Timeframe).TotalMinutes.ToInt();
      //var ticksLocal = _ticks.Where(t => t.StartDate >= Timeframe);
      //RegressionCoefficients = SetTicksPrice(ticksLocal, 1, readFrom, writeTo);
      ShowTicks();
      sw.Reset(); sw.Start();
      decisionTime = DateTime.Now - dTotal;
      MinutesBackSpeed = decisionTime.TotalSeconds;
      //if (!RsiTuneUpScheduler.IsRunning)
      //  RsiTuneUpScheduler.Command = () => TuneRsiTicks();
    }

    private void TuneRsiTicks() { TuneRsiTicks(100,0); }
    private void TuneRsiTicks(int From,int ReEntranceCount) {
      int rt = int.MaxValue;
      var ticksCopy = new Rate[_ticks.Count];
      _ticks.CopyTo(ticksCopy);
      var countByTime = Fractals.Length < ui.RsiWavesForCorrelation ? ticksCopy.Length / 2 : TicksInTimeFrame.Count();
      for (int interval = 100; interval > 0 && rt > 0; interval /= 10) {
        rt = TuneRsiTicks(ticksCopy, countByTime, From, interval,ReEntranceCount);
        From = rt - interval;
      }
      if (rt == 0) return;
      rsiTicks = Math.Abs(rt);
    }
    private int TuneRsiTicks(Rate[] ticksCopy,int countByTime, int from, int interval,int reEntranceCount) {
      //System.Threading.Thread.CurrentThread.Priority = ThreadPriority.Lowest;
      var rsiMaxTicks = ui.ticksBack;
      var sw = System.Diagnostics.Stopwatch.StartNew();
      double? corr1Max = -1, corr2Max = -1;
      var increment = interval / 20.0;
      try {
        for (var i = 0; from < rsiMaxTicks; from += interval + (++i * increment).ToInt()) {
          var skip = ticksCopy.Length - countByTime - from;
          if (skip <= 10) break;
          var ticksRsi = ticksCopy.Skip(skip).ToArray();
          ticksRsi.Rsi1(from, true);
          double corr1, corr2;
          GetCorrelations(ticksRsi, r => r.PriceRsi1, out corr1, out corr2);
          if (corr1Max.HasValue && corr1Max > .3 && corr1Max > corr1 && corr2Max > corr2) {
            double corr1Curr, corr2Curr;
            GetCorrelations(TicksInTimeFrame.ToArray(), r => r.PriceRsi, out corr1Curr, out corr2Curr);
            if (rsiTicks == 0 || corr1Curr < corr1Max || corr2Curr < corr2Max) {
              Log = "Corr:[" + countByTime + "]" + corr1Max.Value.ToString("n2") + "," + corr2Max.Value.ToString("n2") + " in " + from + " ticks, took " + sw.ElapsedMilliseconds + " ms.";
              return from;
            }
            Log = string.Format("Corr:[Old]{0:n2} > {1:n2} in {2:n0}", corr1Curr, corr1Max, from);
            if (reEntranceCount == 0)
              TuneRsiTicks((rsiTicks * .75).ToInt(), ++reEntranceCount);
            return 0;
          }
          corr1Max = corr1;
          corr2Max = corr2;
        }
        Log = "Rsi tuner aborted at " + from + " ticks back.";
        return -from;
      }catch(Exception exc){
        Log = exc;
        return 0;
      } finally {
      }
    }

    private void GetCorrelations(Rate[] ticksRsi,Func<Rate,double?>GetRsiValue, out double corr1, out double corr2) {
      var ts = ticksRsi.Where(r => GetRsiValue(r).GetValueOrDefault(50) != 50 ).ToArray();
      var x = ts.Select(r => r.PriceClose).ToArray();
      var y = ts.Select(r=>GetRsiValue(r).Value).ToArray();
      corr1 = alglib.correlation.pearsoncorrelation(ref x, ref y, x.Length);
      corr2 = alglib.correlation.spearmanrankcorrelation(x, y, x.Length);
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
      if (priceHeightMax != priceHeightMaxOld || priceHeightMin != priceHeightMinOld) {
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
            RegressionCoefficients = wisUpDown.First().Coeffs;
            MinutesBackSampleCount = wisUpDown.Length;
          } else {
            Timeframe = ret = wisWaveRatio.First().StartDate;
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
    Func<Rate, bool> hasRsi = rate => rate.PriceRsi.HasValue;
    Func<Rate, bool> hasRsi1 = rate => rate.PriceRsi1.HasValue;
    private Rate[] TicksInTimeFrame {
      get {
        {
          if (true || _ticksInTimeFrame.Length == 0) {
            var logHeader = "TIF "; var dateNow = DateTime.Now; int step = 0; Func<string> timeSpan = () => logHeader + " : " + (step++) + " " + (DateTime.Now - dateNow).TotalMilliseconds;
            if (Ticks == null || Ticks.Count == 0) return _ticksInTimeFrame;
            Rate[] ticks;
            lock (_ticks) {
              ticks = _ticks.Where(t => t.StartDate >= Timeframe).ToArray();
            }
            if (ticks.Count() == 0) return _ticksInTimeFrame;
            var tickLast = ticks.Last();

            //if (false) {
            //  VolatilityUp = GetVolatility(ticks, t => t.BidHigh - t.PriceAvg1, ui.volatilityWieght1);
            //  VolatilityDown = GetVolatility(ticks, t => t.PriceAvg1 - t.AskLow, ui.volatilityWieght1);
            //  VolatilityUpPeak = GetVolatility(ticks, t => t.BidHigh - t.PriceAvg1, 0);
            //  VolatilityDownPeak = GetVolatility(ticks, t => t.PriceAvg1 - t.AskLow, 0);
            //  RaisePropertyChanged(() => WavesRatio);
            //  PriceHeight = GetPriceHeight1(tickLast);
            //}

            if (drawWaves) {
              SetTicksPrice(ticks, ui.wavePolynomeOrder, r => r.PriceAvg, (tick, price) => tick.PriceAvg2 = price);
              waves = Signaler.GetWaves(ticks.Select((t, ti) => new Signaler.DataPoint(t.PriceAvg2, t.StartDate, 0, ti)).ToArray());
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
            _ticksInTimeFrame = ticks;
            VLog = timeSpan();
            //var voltsDateStart = ServerTime.AddMinutes(-15);
            //Voltages = Signaler.GetVoltageByTick(_ticksInTimeFrame.Where(t => t.StartDate > voltsDateStart), 10);
          }
          return _ticksInTimeFrame;
        }
      }
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
      var coeffs = Lib.Regress(ticks.Select(readFrom).ToArray(), polyOrder);
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
            //ticks.Add(new Tick(serverTime, price.Ask, price.Bid, 0, false));
            //Ticks.FillRSI(14, getPrice, getRsi, setRsi);
            GetTicksAsync();
          } else {
            var lastTickTime = serverTime.Round().AddMinutes(-1);
            if ((lastTickTime - _ticks.Last().StartDate).TotalMilliseconds > 0)
              GetTicksAsync(ticksStartDate, lastTickTime);
            if (lastBar.StartDate.AddMinutes(1) < serverTime)
              lastBar = new Rate(false);
            lastBar.AddTick(priceTime.Round(), price.Ask, price.Bid);
          }
          FillRsis();
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
    //Dimok: Close all Sum(PL)>profitMin positions
    //Dimok: TicksForRsi,RsiPeriodMin parameters for FillRsis()
    //Dimok: TradeAdd???
    //Dimok: Fractals based on Rsi
    void ShowTicks() {
      if (CorridorsWindow_EURJPY == null)
        Log = "CorridorsWindow_EURJPY not found!";
      else if (RsiStats!=null && !CorridorsScheduler.IsRunning)
        CorridorsScheduler.Command = () => {
          var ticksInTimeFrame = TicksInTimeFrame;
          var rsi = ticksInTimeFrame.Where(hasRsi).ToArray().GetMinuteTicks(1).Select(t => new Volt() { Volts = t.PriceRsi.GetValueOrDefault(), StartDate = t.StartDate }).ToList();
          if (rsi.Count == 0)
            rsi = ticksInTimeFrame.Select(t => new Volt() { Volts = t.PriceRsi.GetValueOrDefault(50), StartDate = t.StartDate }).ToList();
          if (ticksInTimeFrame.Length < 10) return;
          CorridorsWindow_EURJPY.AddTicks(
            null,
            ticksInTimeFrame.ToList(),
            rsi,
            RsiStats.Sell,
            RsiStats.Buy,
            rsi.Average(r => r.Volts),
            0, 0, 0,
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
      tradeStats.ticksPerMinuteCurr = TicksPerMinuteCurr;
      tradeStats.ticksPerMinutePrev = TicksPerMinutePrev;
      tradeStats.ticksPerMinuteLong = TicksPerMinuteAverageLong;
      tradeStats.legUpInPips = legUpAverageInPips;
      tradeStats.legDownInPips = legDownAverageInPips;
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
      if (!CanTrade) response.GoBuy = response.GoSell = false;
      //if (tr.tradesBuy.Length > 0 && tr.closeOnProfitOnly && !tr.tradesBuy.Any(t => t.PL > tr.profitMin)) response.GoSell = false;
      //if (tr.tradesSell.Length > 0 && tr.closeOnProfitOnly && !tr.tradesSell.Any(t => t.PL > tr.profitMin)) response.GoBuy = false;
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
    public double PricePosition { get; set; }
    public bool IsPriceInPosition { get; set; }
    public void Decisioner(Order2GoAddIn.Price eventPrice, TradeRequest tr, TradeResponse ti) {
      try {
        var price = eventPrice ?? priceCurrent;
        #region Log Helper
        var logHeader = "D"; var dateNow = DateTime.Now; Func<string, string> timeSpan = step => logHeader + " : " + (DateTime.Now - dateNow).TotalMilliseconds + " - " + step;
        if (fw == null || fw.Desk == null || TestMode) return;
        VLog = timeSpan("Start");
        var ticksInTimeFrame = TicksInTimeFrame;
        VLog = timeSpan("Ticks");
        #endregion
        #region Fractal Angle
        var angleCanBuy = Angle.Between(-tr.tradeAngleMax, -tr.tradeAngleMin);
        var angleCanSell = Angle.Between(tr.tradeAngleMin, tr.tradeAngleMax);

        var fractalWaveCanBuy = Fractals.Length > 1 && !isInSell && angleCanBuy;
        //(price.Bid <= Fractals[2].AskHigh 
        //|| (angleCanBuy && Fractals.Length > 3 && Fractals[0].AskHigh >= Fractals[3].BidLow));
        var fractalWaveCanSell = Fractals.Length > 1 && isInSell && angleCanSell;
        //(price.Ask >= Fractals[2].BidLow 
        //|| (angleCanSell && Fractals.Length > 3 && Fractals[0].BidLow <= Fractals[3].AskHigh));

        FractalWaveBuySellColor = fractalWaveCanBuy ? true : fractalWaveCanSell ? (bool?)false : null;
        #endregion

        if (ticksInTimeFrame.Length == 0 || MinutesBackSampleCount == 0) {
          ti.CorridorOK = false;
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
        var densityFoo_9 = new Func<bool, double>((buy) => {
          return SpreadAverageInPips;
        });
        var densityFoos = new[] { densityFoo_0, densityFoo_1, densityFoo_2, densityFoo_3, densityFoo_4, densityFoo_5, densityFoo_6, densityFoo_7, densityFoo_8, densityFoo_9 };
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
          CanTrade = true;
          if (RsiLocalMax == null || RsiStats == null) {
            CanTrade = false;
            return;
          }
          IsRsiSell = RsiLocalMax.PriceRsi >= RsiStats.Sell;
          IsRsiBuy = RsiLocalMin.PriceRsi <= RsiStats.Buy;

          goBuy = AreAnglesBuy && IsRsiBuy;
          goSell = AreAnglesSell && isRsiSell;

          BuySell = goBuy ? true : goSell ? false : (bool?)null;

          ti.DencityRatio = densityFoos[tr.densityFoo](goBuy);
          ti.DencityRatioBuy = densityFoos[tr.densityFoo](true);
          ti.DencityRatioSell = densityFoos[tr.densityFoo](false);
          ti.FractalDatesBuy = rsiFractals.Where(f => f.HasFractalBuy).Select(f => f.StartDate).ToArray();
          ti.FractalDatesSell = rsiFractals.Where(f => f.HasFractalSell).Select(f => f.StartDate).ToArray();

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
        CanTrade = false;
        Log = exc;
      } finally {
        RaisePropertyChanged( () => IsPowerDown, () => PricePosition, () => IsPriceInPosition);
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

      #region closeAllOnTrade
      if (tr.closeAllOnTrade) 
      {
        var spreadInPips = -fw.InPips(priceCurrent.Spread);
        if ((true || ti.LotsToTradeSell <= 1) && tr.tradesSell.Length > 0) {
          var hasProfit = true || tr.closeOnNet ? tr.SellNetPLPip > spreadInPips : tr.tradesSell.Count(t => t.PL > spreadInPips) > 0;
          if (!hasProfit)
            ti.LotsToTradeBuy = tr.tradesSell.Max(t => t.Lots) * 2;
        }
        if ((true || ti.LotsToTradeBuy <= 1) && tr.tradesBuy.Length > 0) {
          var hasProfit = true || tr.closeOnNet ? tr.BuyNetPLPip > spreadInPips : tr.tradesBuy.Count(t => t.PL > priceCurrent.Spread) > 0;
          if (!hasProfit)
            ti.LotsToTradeSell = tr.tradesBuy.Max(t => t.Lots) * 2;
        }
      }
      #endregion

      #region Rid of Old Positions
      bool doShortStack = Math.Min(tr.SellPositions, tr.BuyPositions) > tr.shortStack;
      bool doTrancate = Math.Min(tr.SellPositions, tr.BuyPositions) >= tr.shortStack + tr.shortStackTruncateOffset; ;
      var closeBuyIDs = new List<O2G.Trade>();
      var closeSellIDs = new List<O2G.Trade>();
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
            closeSellIDs.AddRange(tr.tradesSell);
            break;
          }
          var plMin = profitMin(tr.tradesSell, 3);
          if (tr.SellNetPLPip >= Math.Abs(plMin)) {
            closeSellIDs.AddRange(tr.tradesSell);
            break;
          }
          closeSellIDs.AddRange(tr.tradesSell.Where(t => t.PL >= plMin));
          if (closeSellIDs.Count() > 0) break;
          if (!tr.closeOnProfitOnly ){
            if (doShortStack || tr.BuyPositions > tr.SellPositions - tr.closeOppositeOffset)
              closeSellIDs.Add(tr.tradesSell.OrderBy(t => t.Time).FirstOrLast(tr.sellOnProfitLast));
            if (doTrancate && (tr.SellPositions - tr.BuyPositions).Between(0, 1)) {
              closeSellIDs.AddRange(tr.tradesSell);
              closeBuyIDs.AddRange(tr.tradesBuy.OrderByDescending(t => t.Time).Skip(leaveOpenAfterTruncate));
            }
          }
          break;
        }
        while (!tr.tradeAdded.Buy && tr.BuyPositions > 0) {
          if (doShortStack && tr.closeAllOnTrade) {
            closeBuyIDs.AddRange(tr.tradesBuy);
            break;
          }
          var plMin = profitMin(tr.tradesBuy, 3);
          if (tr.BuyNetPLPip >= Math.Abs(plMin)) {
            closeBuyIDs.AddRange(tr.tradesBuy);
            break;
          }
          closeBuyIDs.AddRange(tr.tradesBuy.Where(t => t.PL >= plMin));
          if (closeBuyIDs.Count() > 0) break;
          if (!tr.closeOnProfitOnly) {
            if (doShortStack || tr.SellPositions > tr.BuyPositions - tr.closeOppositeOffset)
              closeBuyIDs.Add(tr.tradesBuy.OrderBy(t => t.Time).FirstOrLast(tr.sellOnProfitLast));
            if (doTrancate && (tr.BuyPositions - tr.SellPositions).Between(0, 1)) {
              closeBuyIDs.AddRange(tr.tradesBuy);
              closeSellIDs.AddRange(tr.tradesSell.OrderByDescending(t => t.Time).Skip(leaveOpenAfterTruncate));
            }
          }
          break;
        }
        tr.tradeAdded = null;
        if (closeSellIDs.Concat(closeBuyIDs).Any(t => t.PL < -1)) {
          var i = 0;
        }
      }
      #endregion

      #region Close on time
      if (false) {
        Func<Trade, bool> closeByTime = t => {
          var fs = Fractals.Take(3);
          return t.Time < fw.ServerTimeCached.Subtract((fs.First().StartDate - fs.Last().StartDate).Duration()) && t.PL > 0;
        };
        if (A <= 0 && A1 <= 0) closeBuyIDs.AddRange(tr.tradesBuy.Where(closeByTime));
        if (A1 >= 0) closeSellIDs.AddRange(tr.tradesSell.Where(closeByTime));
      }
      #endregion

      #region Colose on Net
      ///Dimok: Close two positions if sum PL > 0
      if (tr.closeOnNet) {
        if (tr.tradesBuy.Length + tr.tradesSell.Length >= 4 && tr.BuyNetPLPip + tr.SellNetPLPip > 0) {
          closeBuyIDs.AddRange(tr.tradesBuy);
          closeSellIDs.AddRange(tr.tradesSell);
        } else {
          Func<Trade[], double, double> plNetMin = (trades, leg) => trades.Length == 0 ? 0 : leg / (trades.Sum(t => t.Lots) / trades.Min(t => t.Lots));
          if (tr.tradesBuy.Length > 1 && AreAnglesSell) {
            closeBuyIDs.AddRange(tr.tradesBuy.NetProfitTrades(tr.profitMin));
          }
          if (tr.tradesSell.Length > 1 && AreAnglesBuy) {
            closeSellIDs.AddRange(tr.tradesSell.NetProfitTrades(tr.profitMin));
          }
        }
      }
      #endregion

      #region Colose on Corridor
      if (tr.closeOnCorridorBorder.HasValue && priceCurrent != null && legDownAverageInPips > 0 && legUpAverageInPips > 0) {
        if (tr.closeOnCorridorBorder.Value && (!ui.tradeByFractal || PositionHelper.Position.Between(ui.peakTradeMarginLow, ui.peakTradeMarginHigh))) {
          closeBuyIDs.AddRange(
            tr.tradesBuy.Where(
            t => t.PL >= tr.profitMin && AreAnglesSell
              //ch.Up && (isInSell && isPosBSOk || ch.RangeInPips >= legUpAverageInPips)
              //&& A < 0 && ch.BuyPL(t.Open) >= tr.profitMin
              //&& (t.PL > 0 || ch.okToLoose(t))
            ));
          closeSellIDs.AddRange(
            tr.tradesSell.Where(
            t => t.PL >= tr.profitMin && AreAnglesBuy
              //ch.Down && (isInBuy && isPosBSOk || ch.RangeInPips >= legDownAverageInPips)
              //&& A > 0 && ch.SellPL(t.Open) >= tr.profitMin
              //&& (t.PL > 0 || ch.okToLoose(t))
            ));
        } else {
          closeBuyIDs.AddRange(tr.tradesBuy.Where(t => t.PL > legUpAverageInPips/* t.Remark.TradeWaveHeight*/));
          closeSellIDs.AddRange(tr.tradesSell.Where(t => t.PL > legDownAverageInPips/* t.Remark.TradeWaveHeight*/));
        }
        if (closeSellIDs.Concat(closeBuyIDs).Any(t => t.PL < -1)) {
          var i = 0;
        }
      }
      #endregion

      #region RSI
      if (tr.rsiProfit != 0) {
        closeBuyIDs.AddRange(tr.tradesBuy.Where(t => t.PL >= tr.profitMin && RsiLocalMax.PriceRsi >= RsiStats.Sell + tr.rsiProfit));
        closeSellIDs.AddRange(tr.tradesSell.Where(t => t.PL >= tr.profitMin && RsiLocalMin.PriceRsi <= RsiStats.Buy - tr.rsiProfit));

        if (false && ti.RsiHigh >= tr.rsiTresholdSell)
          closeBuyIDs.AddRange(tr.tradesBuy.Where(t => t.PL >= tr.rsiProfit));
        if (false && ti.RsiLow <= tr.rsiTresholdBuy)
          closeSellIDs.AddRange(tr.tradesSell.Where(t => t.PL >= tr.rsiProfit));
      }
      #endregion

      #region CloseIfProfitTradesMoreThen
      if (tr.closeIfProfitTradesMoreThen > 0) {
        Func<Trade[], int> tradesCount =
          (trades) => tr.closeProfitTradesMaximum > 0 ? tr.closeProfitTradesMaximum : trades.Length + tr.closeProfitTradesMaximum;
        if (tr.tradesBuy.Length > tr.closeIfProfitTradesMoreThen) {
          var plMin = Math.Ceiling(profitMin(tr.tradesBuy, 2));
          var closeBuys = tr.tradesBuy.Where(t => t.PL >= plMin).ToArray();
          if (closeBuys.Length > tr.closeIfProfitTradesMoreThen) {
            var tc = tradesCount(closeBuys);
            closeBuyIDs.AddRange(closeBuys.OrderByDescending(t => t.GrossPL).Take(tc));
          }
        }
        if(tr.tradesSell.Length > tr.closeIfProfitTradesMoreThen){
          var plMin = Math.Ceiling(profitMin(tr.tradesSell, 2));
          var closeSells =  tr.tradesSell.Where(t => t.PL >= plMin).ToArray();
          if (closeSells.Length > tr.closeIfProfitTradesMoreThen) {
            var tc = tradesCount(closeSells);
            closeSellIDs.AddRange(closeSells.OrderByDescending(t => t.GrossPL).Take(tc));
          }
        }
        if (closeSellIDs.Concat(closeBuyIDs).Any(t => t.PL < -1)) {
          var i = 0;
        }
      }
      #endregion

      #region Finalize
      if (closeBuyIDs.Count > 0 && tr.BuyNetPLPip > 0)
        closeBuyIDs.AddRange(tr.tradesBuy);
      if (closeSellIDs.Count > 0 && tr.SellNetPLPip > 0)
        closeSellIDs.AddRange(tr.tradesSell);
      ti.CloseTradeIDs = closeBuyIDs.Concat(closeSellIDs).Select(t=>t.Id).Distinct().ToArray(); 
      #endregion

      if (closeSellIDs.Concat(closeBuyIDs).Any(t => t.PL < -1)) {
        var i = 0;
      }

      #endregion
    }

    #endregion

    public class CloseByPositionHelper {
      Rate minTick;
      Rate maxTick;
      FXW fw;
      Rate[] ticks;
      O2G.Price price;
      Rate lastFractal { get { return new[] { maxTick, minTick }.OrderBars().Last(); } }
      double spread { get { return maxTick.PriceHigh - minTick.PriceLow; } }
      Rate _localPeak;
      Rate _tail;
      Rate localPeak {
        get {
          if (_localPeak!=null) return _localPeak;
          var ts = ticks.Where(t => t.StartDate >= lastFractal.StartDate);
          return _localPeak = Up ? ts.OrderBy(t => t.PriceLow).First() : ts.OrderBy(t => t.PriceHigh).Last();
        }
      }
      Rate tail {
        get {
          if ( _tail != null) return _tail;
          var ts = ticks.Where(t => t.StartDate >= localPeak.StartDate);
          return _tail = Down ? ts.OrderBy(t => t.PriceLow).First() : ts.OrderBy(t => t.PriceHigh).Last();
        }
      }
      double hook { get { return Down ? localPeak.PriceHigh - tail.PriceLow : tail.PriceHigh - localPeak.PriceLow; } }
      public double hookSize { get { return hook / spread; } }
      public bool Up { get { return maxTick.StartDate > minTick.StartDate; } }
      public bool Down { get { return !Up; } }
      public double PositionGlobal {
        get {
          if (price == null || maxTick == null) return -1;
          var position = price.Average.Position(maxTick.PriceHigh, minTick.PriceLow);
          return Down ? position : 1 - position;
        }
      }
      public double Position { get { return ticks == null ? 0 : hookSize; } }
      public CloseByPositionHelper(FXW fw) {
        this.fw = fw;
      }
      public void Fill(O2G.Price price, IEnumerable<Rate> Ticks,Rate OldestFractal) {
        ///Dimok: deffer ticksSinceLastFractal
        this.ticks = Ticks.ToArray().Where(t=>t.StartDate>= OldestFractal.StartDate).OrderBy(t => t.PriceAvg).ToArray();
        this.minTick = ticks.First();
        this.maxTick = ticks.Last();
        this.price = price;
        _localPeak = null;
        _tail = null;
      }
      public bool okToLoose(O2G.Trade trade) { return fw.ServerTimeCached - trade.Time > TimeSpan.FromMinutes(1); }
      public double SellPL(double price) { return fw.InPips(price - minTick.AskLow); }
      public double BuyPL(double price) { return fw.InPips(maxTick.BidHigh - price); }
    }


    #region FX Event Handlers
    void fxCoreWrapper_EURJPY_PriceChanged(Order2GoAddIn.Price price) {
      Price = price;
      lock (_ticks) {
        ProcessPrice(price, ref _ticks);
      }
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
      Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => {
        CorridorsWindow_EURJPY.WindowState = System.Windows.WindowState.Normal;
        CorridorsWindow_EURJPY.Activate();

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
    public override void Checked(object sender, RoutedEventArgs e) {
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

    private void Window_Closing_Hide(object sender, System.ComponentModel.CancelEventArgs e) {
      e.Cancel = true;
      Application.Current.Dispatcher.BeginInvoke(
        DispatcherPriority.Background,
        (DispatcherOperationCallback)delegate(object o) {
        Hide();
        return null;
      },
          null);

    }
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
      if (HedgeHog.Server.App.serverWindows[0] == this)
        Dispatcher.Invoke(new Action(() => {
          if (fw != null)
            fw.Dispose();
          if (CorridorsWindow_EURJPY != null)
            CorridorsWindow_EURJPY.Close();
        }));
      else
        Window_Closing_Hide(sender, e);
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

    Server.App app { get { return Application.Current as Server.App; } }
    private void OpenMainWindow(object sender, RoutedEventArgs e) {
      Server.App.AddMainWindow(null);
    }
  }
}
