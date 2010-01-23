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

    public string TitleAndPort { get { return "HedgeHog Server : " + tcpPort+" -> "+cmbPair.Text; } }


    static bool drawWaves { get { return false; } }

    private class UI {
      public bool _chkIsDemo;
      public bool isDemo { get { return _chkIsDemo; } }

      public bool _chkVerboseLogging;
      public bool verboseLogging { get { return _chkVerboseLogging; } }

      public bool? _chkCorridorByMinimumVolatility;
      public bool? corridorByMinimumVolatility {
        get { return _chkCorridorByMinimumVolatility.HasValue ? _chkCorridorByMinimumVolatility : null; }
        set { _chkCorridorByMinimumVolatility = value.HasValue ? value : null; }
      }

      public int _txtVolatilityWieght;
      public int volatilityWieght { get { return _txtVolatilityWieght; } }

      public int _txtVolatilityWieght1;
      public int volatilityWieght1 { get { return _txtVolatilityWieght1; } }

      public int _txtWavePolynomeOrder;
      public int wavePolynomeOrder { get { return _txtWavePolynomeOrder; } }

      public double _txtWaveRatioMinimum;
      public double waveRatioMinimum { get { return _txtWaveRatioMinimum; } }
      public int _txtHighMinutesHoursBack;
      public int highMinutesHoursBack { get { return _txtHighMinutesHoursBack; } }

      public int _txtCorridorHeightMinutes;
      public int corridorHeightMinutes { get { return _txtCorridorHeightMinutes; } }

      public int _txtCorridorMinimumPercent;
      public int corridorMinimumPercent { get { return _txtCorridorMinimumPercent; } }


      public int _txtTimeFrameMinutesMinimum;
      public int timeFrameMinutesMinimum { get { return _txtTimeFrameMinutesMinimum; } }

      public int _txtTimeFrameMinutesMaximum;
      public int timeFrameMinutesMaximum { get { return _txtTimeFrameMinutesMaximum; } }

      public double _txtTimeFramePercInterval;
      public double timeFramePercInterval { get { return _txtTimeFramePercInterval / 100.0; } }

      public double _txtFractalMinutes;
      public TimeSpan fractalMinutes { get { return TimeSpan.FromMinutes(_txtFractalMinutes); } }
            

      public double _txtTimeFramesFibRatioMaximum;
      public double timeFramesFibRatioMaximum { get { return _txtTimeFramesFibRatioMaximum; } }

      public bool _chkCorridorByUpDownRatio;
      public bool corridorByUpDownRatio { get { return _chkCorridorByUpDownRatio; } }

      public bool _chkDoRsi;
      public bool doRsi { get { return _chkDoRsi; } }

      public string _txtAcount;
      public string Account { get { return _txtAcount; } }

      public int _txtTicksBack;
      public int ticksBack { get { return _txtTicksBack; } }

      public int _txtWavesCountBig;
      public int wavesCountBig { get { return _txtWavesCountBig; } }

      public int _txtWavesCountSmall;
      public int wavesCountSmall { get { return _txtWavesCountSmall; } }

      public string _txtTimeFrameTimeStart;
      public DateTime timeFrameTimeStart {
        get {
          var s = _txtTimeFrameTimeStart + "";
          DateTime d;
          if (Regex.IsMatch(s, @"\d+/\d+/\d{2,} \d\d:\d\d(:\d\d)?") && DateTime.TryParse(s + "", out d)) return d;
          return DateTime.FromOADate(0);
        }
      }
      public bool _chkSaveVoltageToFile;
      public bool SaveVoltageToFile { get { return _chkSaveVoltageToFile; } }

      public bool _chkGroupTicks;
      public bool groupTicks { get { return _chkGroupTicks; } }

      public bool _chkCachePriceHeight;
      public bool cachePriceHeight { get { return _chkCachePriceHeight; } }

    }

    private UI ui = new UI();

    #endregion

    #region Properties
    string logFileName = "Log.txt";
    public static string CurrentDirectory { get { return AppDomain.CurrentDomain.BaseDirectory; } }
    static Order2GoAddIn.CoreFX coreFX { get { return (Application.Current as Server.App).CoreFX; } }
    Order2GoAddIn.FXCoreWrapper fw;
    List<Order2GoAddIn.FXCoreWrapper.Rate> _ticks = new List<Order2GoAddIn.FXCoreWrapper.Rate>();
    List<Order2GoAddIn.FXCoreWrapper.Rate> Ticks {
      get { return doTicks || lastBar.Count == 0 ? _ticks : _ticks.Concat(new[] { lastBar }).ToList(); }
      set { _ticks = value; }
    }
    List<Volt> Voltages = new List<Volt>();
    List<FXW.Rate> RatesHigh = new List<Order2GoAddIn.FXCoreWrapper.Rate>();
    Corridors CorridorsWindow_EURJPY;
    ThreadScheduler VoltageScheduler;
    ThreadScheduler TicksScheduler;
    ThreadScheduler SaveToFileScheduler;
    ThreadScheduler VolatilityScheduler;
    ThreadScheduler DecisionScheduler;
    Scheduler CorridorsScheduler;
    ThreadScheduler MinutesBackScheduler;
    Func<FXW.Rate, double> spreadLambda = r => r.Spread;
    FXW.Rate tickHigh, tickLow;
    #endregion

    #region Statistics
    public double Angle {
      get { return Math.Atan(A) * (180 / Math.PI); }
    }
    public O2G.Price Price { get; set; }
    public DateTime ServerTime {
      get { return fw == null ? DateTime.Now : fw.ServerTime; }
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
        if (IsBuyMode == null) FractalBuy = FractalSell = new FXW.Rate();
      }
    }

    private FXW.Rate _FractalBuy;
    public FXW.Rate FractalBuy {
      get { return _FractalBuy; }
      set { if (_FractalBuy == value)return; _FractalBuy = value; RaisePropertyChangedCore(); }
    }
    private bool _FractalBuyColor;
    public bool FractalBuyColor {
      get { return _FractalBuyColor; }
      set { _FractalBuyColor = value; RaisePropertyChangedCore(); }
    }

    private FXW.Rate _FractalSell;
    public FXW.Rate FractalSell {
      get { return _FractalSell; }
      set { if (_FractalSell == value)return; _FractalSell = value; RaisePropertyChangedCore(); }
    }

    private bool _FractalSellColor;
    public bool FractalSellColor {
      get { return _FractalSellColor; }
      set { _FractalSellColor = value; RaisePropertyChangedCore(); }
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
    double A;
    double[] _regressionCoeffs = new double[] { };

    double[] RegressionCoefficients {
      get { return _regressionCoeffs; }
      set {
        _regressionCoeffs = value;
        A = value[1];
        RaisePropertyChanged(() => Angle);
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
    Func<FXW.Rate, double> heightLambda = new Func<FXW.Rate, double>(t => t.PriceAvg - t.PriceAvg1);



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
      InitializeComponent();
      System.IO.File.Delete(logFileName);
      Closing += new System.ComponentModel.CancelEventHandler(ServerWindow_Closing);
      coreFX.LoggedInEvent += new EventHandler<EventArgs>(coreFX_LoggedInEvent);
      coreFX.LoginError += new Order2GoAddIn.CoreFX.LoginErrorHandler(coreFX_LoginError);
      fw = new Order2GoAddIn.FXCoreWrapper(coreFX, cmbPair.Text);
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
    void GetTicks(DateTime StartDate, DateTime EndDate) {
      try {
        if (StartDate == fxDateNow) StartDate = ticksStartDate;
        StartDate = Lib.Min(timeFrameDateStart, StartDate);
        if (ratePeriod == 0 && _ticks.Count == 0)
          Ticks = fw.GetTicks(ui.ticksBack).OfType<FXW.Rate>().ToList();
        List<FXW.Rate> ticks = _ticks.Where(b => b.IsHistory).ToList();
        fw.GetBars(ratePeriod, StartDate, EndDate, ref ticks);
        Ticks = ticks.OrderBars().ToList();
        var timer = DateTime.Now;
        Ticks.ToArray().FillRSI(14, getPrice, getRsi, setRsi);
        Debug.WriteLine("Rsi time:" + (DateTime.Now - timer).TotalSeconds);
        RaisePropertyChanged(() => TimeframeByTicksMin);
        ShowTicks();
      } catch (Exception exc) {
        Log = exc;
      }
      //Select((b, i) => new Order2GoAddIn.FXCoreWrapper.Tick() {
      //  Ask = b.Ask, Bid = b.Bid, StartDate = b.StartDate, Row = i + 1, IsHistory = b.IsHistory
      //}).ToList();
    }
    Func<FXW.Rate, double> getPrice = r => r.PriceClose;
    Func<FXW.Rate, double?> getRsi = r => r.PriceRsi;
    Action<FXW.Rate, double?> setRsi = (r, d) => r.PriceRsi = d;
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
        var tick = Ticks.Reverse<FXW.Rate>().Take(ui.ticksBack).LastOrDefault();
        return new[] { dateMin, tick == null ? dateMin : tick.StartDate, ServerTime.AddMinutes(-ui.timeFrameMinutesMaximum) }.Min();
      }
    }
    private static void SaveRateToFile(List<Order2GoAddIn.FXCoreWrapper.Rate> ticks, Func<FXW.Rate, double> value1, Func<FXW.Rate, double> value2, Func<FXW.Rate, double> value3) {
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
      public WaveInfo(DateTime startDate, double waveRatio,double tradePosition, double upDownRatio, double vu, double vd, double[] coeffs, TimeSpan interval) {
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
    Func<FXW.Rate, double> readFrom = r => r.PriceAvg;
    bool IsTsiWaiveHigh(FXW.Rate rate, double average, double stDev) { return rate.PriceTsi > average + stDev; }
    bool IsTsiWaiveLow(FXW.Rate rate, double average, double stDev) { return rate.PriceTsi < average-stDev; }
    bool IsTsiWaive(FXW.Rate rate, double average, double stDev) { return IsTsiWaiveHigh(rate, average, stDev) || IsTsiWaiveLow(rate, average, stDev); }
    static public class Extension {
    }

    FXW.Rate GetFractalWave(FXW.Rate[] ratesFractal, int waveCountMinimum) {
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

    FXW.Rate GetTsiWave(FXW.Rate[] ratesTsi, int waveCountMinimum) {
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
    IEnumerable<FXW.Rate> TicksForTimeFrame {
      get {
        return
          (doTicks ? ui.groupTicks ? Ticks.GroupTicksToRates() : Ticks : Ticks);
      }
    }
    void CalculateTimeFrame(object o) {
      var paramArray = ((object[])o);
      var tickLocal = (FXW.Rate)paramArray[0];
      var eventLocal = (ManualResetEvent)paramArray[1];
      var ticksCopy = (List<FXW.Rate>)paramArray[2];
      var writeTo = (Action<FXW.Rate, double>)paramArray[3];
      var tickLast = (FXW.Rate)paramArray[4];
      var wi = (List<WaveInfo>)paramArray[5];
      CorridorSpreadMinimum = SpreadByBarPeriod(ui.corridorHeightMinutes, false);
      try {
        while (true) {
          var coeffs = SetTicksPrice(ticksCopy, 1, readFrom, writeTo);
          var tickHigh = ticksCopy.OrderByDescending(t => t.PriceHigh).First();
          var tickLow = ticksCopy.OrderBy(t => t.PriceLow).First();
          var vu = GetVolatilityUp(ticksCopy.ToList(), ui.volatilityWieght);
          var vd = GetVolatilityDown(ticksCopy.ToList(), ui.volatilityWieght);
          var vh = vu + vd;
          if (vh < CorridorSpreadMinimum) break;
          var priceHeightUp = priceHeightMax - tickLast.PriceAvg4;
          var priceHeightDown = tickLast.PriceAvg4 - priceHeightMin;
          ////corridorHeightMin = fooCorrHeightMinimum(tickHigh.PriceHigh, tickLow.PriceLow);
          ////if (Math.Min(vd, vu) < corridorHeightMin) break;
          var upDownRatio = Lib.FibRatio(vu, vd);
          if (upDownRatio > ui.timeFramesFibRatioMaximum) break;
          var posBuy = /*Math.Abs(*/priceHeightDown / vd - 1/*)*/;
          var posSell = /*Math.Abs(*/priceHeightUp / vu - 1/*)*/;
          var poss = new[] { posBuy, posSell };
          //var posTrade = vd == vu || !ui.corridorByMinimumVolatility.HasValue ? Math.Min(posSell, posBuy) : fooPosTradeCondition(vd, vu) ? posBuy : posSell;
          var posTrade = Lib.FibRatio(posBuy, posSell);
          var stDev = ticksCopy.StdDev(t => readFrom(t) - t.PriceAvg4);
          var waveRatio = stDev / (vd + vu);// Math.Min(vd, vh) / (tickHigh.PriceClose - tickLow.PriceClose);
          wi.Add(new WaveInfo(tickLocal.StartDate, Math.Round(waveRatio, 3), Math.Round(posTrade, 2),upDownRatio, /*Math.Abs(vu / vd - vd / vu)*/ vu, vd, coeffs, TimeSpan.Zero/*interval*/));
          break;
        }
      } catch (Exception exc) {
        Log = exc;
      }
      eventLocal.Set();
    }
    void GetMinutesBack() {
      DateTime dTotal = DateTime.Now;
      DateTime ret = timeFrameDateStart;
      var wi = new List<WaveInfo>();
      if (Ticks.Count == 0) return;
      var ticksReversed = TicksForTimeFrame.OrderBarsDescending().ToList();
      Action<FXW.Rate, double> writeTo = (t, p) => t.PriceAvg4 = p;
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
      var corridorSpreadMinimum = CorridorSpreadMinimum = SpreadByBarPeriod(ui.corridorHeightMinutes, false);
      Func<double, double, double> fooCorrHeightMinimum =
        (priceHigh, priceLow) => (priceHigh - priceLow) * (ui.corridorMinimumPercent / 100.0);
      var corridorHeightMin = 0.0;
      Func<double, double, bool> fooPosTradeCondition = NewMethod();
      var ticks = new List<FXW.Rate>();
      var minimumTime = ServerTime.AddMinutes(-Math.Max(TimeframeByTicksMin / 10.0, ui.timeFrameMinutesMinimum));
      {
        //var priceHighTime = doTicks ? ticksReversed.Take(10).Last().StartDate : Ticks.Max(t => t.StartDate).AddMinutes(-3);
        //var tickHT = Ticks.Where(t => t.StartDate > priceHighTime);
        var fractals = _ticks.FillFractals(ui.fractalMinutes).HasFractal(true);
        FractalSell = fractals.Where(r => r.FractalSell != 0).OrderBy(r => r.StartDate).Last();
        FractalBuy = fractals.Where(r => r.FractalBuy != 0).OrderBy(r => r.StartDate).Last();

        priceHeightMax = Ticks.Where(t => t.StartDate >= FractalSell.StartDate).Max(readFrom); //tickHT.Max(readFrom);
        priceHeightMin = Ticks.Where(t => t.StartDate >= FractalBuy.StartDate).Min(readFrom);//tickHT.Min(readFrom);
      }
      if (!ui.cachePriceHeight || priceHeightMax != priceHeightMaxOld || priceHeightMin != priceHeightMinOld) {
        var lastDate = DateTime.MaxValue;
        TimeSpan interval = TimeSpan.Zero;
        List<ManualResetEvent> mreList = new List<ManualResetEvent>();
        int skipMinutes = 0;
        var ratesTsi = Ticks.GetMinuteTicks(1).ToArray();
        ratesTsi.FillTSI((r, d) => r.PriceTsi = d);
        var tsiWave = GetTsiWave(ratesTsi, ui.wavesCountBig);
        if (tsiWave == null) {
          Log = "WaveCount < 2";
          Timeframe = ServerTime;
          return;
        }
        var intervalMinutesOffset = (ServerTime - tsiWave.StartDate).TotalMinutes;
        var tsiStartDate = tsiWave.StartDate;
        foreach (var tick in ticksReversed) {
          ticks.Insert(0, tick);
          if (tick.StartDate > tsiStartDate) continue;
          if (ui.wavesCountBig > 0 && ui.wavesCountSmall > 0) {
            tsiWave = GetTsiWave(ratesTsi.Where(r => r.StartDate > tick.StartDate).ToArray(), ui.wavesCountSmall);
            if (tsiWave == null) continue;
          }
          if (ui.wavesCountSmall > 0) {
            var rateFractal = GetFractalWave(_ticks.Where(r=>r.StartDate>=tick.StartDate).ToArray(), ui.wavesCountSmall);
            if (rateFractal == null) continue;
          }
          var tickLast = ticks.Last();
          interval = TimeSpan.FromMinutes(
            ((tickLast.StartDate - tick.StartDate).TotalMinutes - intervalMinutesOffset) * ui.timeFramePercInterval * 2
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
              var tickLocal = paramArray[0] as FXW.Rate;
              var eventLocal = paramArray[1] as ManualResetEvent;
              var ticksCopy = paramArray[2] as FXW.Rate[];
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
                  if (upDownRatio > ui.timeFramesFibRatioMaximum) break;
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
            if (Lib.FibRatio(VolatilityUp, VolatilityDown) > ui.timeFramesFibRatioMaximum) {
              var i = 0;
            }
          }
          RaisePropertyChanged(() => CorridorSpreadInPips);
          ShowTicks();
        }
        decisionTime = DateTime.Now - dTotal;
        System.Diagnostics.Debug.WriteLine("GetMinutesBack Total:" + decisionTime.TotalMilliseconds);
        MinutesBackSpeed = decisionTime.TotalSeconds;
      }
    }

    private Func<double, double, bool> NewMethod() {
      return (d, u) => ui.corridorByMinimumVolatility.Value ? d < u : d > u;
    }
    TimeSpan decisionTime = TimeSpan.Zero;

    #region GetVolatility ========================================
    static double GetVolatilityUp(List<FXW.Rate> ticks, int weight) {
      return GetVolatility(ticks, t => t.BidHigh - t.PriceAvg4, weight);
    }
    static double GetVolatilityDown(List<FXW.Rate> ticks, int weight) {
      return GetVolatility(ticks, t => t.PriceAvg4 - t.AskLow, weight);
    }
    static double
      GetVolatility(List<FXW.Rate> ticks, Func<FXW.Rate, double> heightLambda, int weight) {
      if (weight == 0) return ticks.Max(heightLambda);
      double volatility = 0;
      Func<FXW.Rate, bool> whereLambda = t => heightLambda(t) > volatility;
      var volTicks = ticks.Where(whereLambda).ToArray();
      double tc = volTicks.Length;
      while (volTicks.Length / tc > weight / 100.0) {
        volatility = volTicks.Average(heightLambda);
        volTicks = volTicks.Where(whereLambda).ToArray();
      }
      return volatility;
    }
    #endregion ========================================================

    FXW.Rate[] _ticksInTimeFrame = new Order2GoAddIn.FXCoreWrapper.Rate[] { };
    double GetPriceHeight1(FXW.Rate rate) {
      //return rate.PriceHigh > rate.PriceAvg1 ? rate.PriceHigh - rate.PriceAvg1 : rate.PriceLow - rate.PriceAvg1;
      var priceHeightAverage = new []{priceHeightMax, priceHeightMin}.Average();
      return rate.PriceAvg > priceHeightAverage ? priceHeightMax - rate.PriceAvg1 : priceHeightMin - rate.PriceAvg1;
    }
    double GetPriceHeight4(FXW.Rate rate) {
      return rate.PriceHigh > rate.PriceAvg4 ? rate.PriceHigh - rate.PriceAvg4 : rate.PriceLow - rate.PriceAvg4;
    }
    private FXW.Rate[] TicksInTimeFrame {
      get {
        {
          if (true || _ticksInTimeFrame.Length == 0) {
            var logHeader = "TIF "; var dateNow = DateTime.Now; int step = 0; Func<string> timeSpan = () => logHeader + " : " + (step++) + " " + (DateTime.Now - dateNow).TotalMilliseconds;
            if (Ticks == null || Ticks.Count == 0) return _ticksInTimeFrame;
            var ticks = _ticks.Where(t => t.StartDate >= Timeframe).ToList();
            //if (ticks.Count < ui.ticksBack) ticks = Ticks.Reverse<FXW.Rate>().Take(ui.ticksBack).Reverse().ToList();
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
    static void SetTicksPrice(IEnumerable<FXW.Rate> ticks, double[] coeffs, Action<FXW.Rate, double> a) {
      double[] yy = new double[ticks.Count()];
      int i1 = 0;
      foreach (var tick in ticks) {
        double y1 = 0; int j = 0;
        coeffs.ToList().ForEach(c => y1 += coeffs[j] * Math.Pow(i1, j++));
        a(tick, y1);// *poly2Wieght + y2 * (1 - poly2Wieght);
        yy[i1++] = y1;
      }
    }
    static double[] SetTicksPrice(IEnumerable<FXW.Rate> ticks, int polyOrder,Func<FXW.Rate,double> readFrom, Action<FXW.Rate, double> writeTo) {
      var coeffs = Regress(ticks.Select(readFrom).ToArray(), polyOrder);
      SetTicksPrice(ticks, coeffs, writeTo);
      return coeffs;
    }

    void CleanTicks() {
      var dateClear = timeFrameDateStart;
      _ticks.Where(t => t.StartDate < dateClear).ToList().ForEach(t => _ticks.Remove(t));
    }
    void GetVolatility() {
      if (ui.timeFrameMinutesMaximum/60.0 > ui.highMinutesHoursBack)
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
    void GetExtreamPrices(TradeRequest req, TradeResponse res) {
      var ticks = req.doTrend ? TicksInTimeFrame : Ticks.ToArray();
      var agvRange = new Func<FXW.Rate, FXW.Rate, bool>(
        (t, thl) => t.StartDate.Between(thl.StartDate.AddSeconds(-req.corridorSmoothSeconds), thl.StartDate.AddSeconds(req.corridorSmoothSeconds)));
      if (req.doTrend) {
        var trendStartDate = waves.Min(w => w.Date);
        var trendEndDate = waves.Max(w => w.Date);
        var dateLambda = new Func<FXW.Rate, bool>(t => t.StartDate.Between(trendStartDate, trendEndDate));
        tickHigh = ticks.Where(dateLambda).OrderBy(heightLambda).Last();
        PeakPriceHigh = res.TradeStats.peakPriceHigh =
        PeakPriceHighAverage = res.TradeStats.peakPriceHighAvg = tickHigh.PriceAvg2;
        //Math.Round(ticks.Where(t => agvRange(t, tickHigh)).Average(t => t.Bid), fw.Digits);

        tickLow = ticks.Where(dateLambda).OrderBy(heightLambda).First();
        ValleyPriceLow = res.TradeStats.valleyPriceLow =
        ValleyPriceLowAverage = res.TradeStats.valleyPriceLowAvg = tickLow.PriceAvg2;
        //Math.Round(ticks.Where(t => agvRange(t, tickLow)).Average(t => t.Ask), fw.Digits);

      } else {
        var dates = new[] { PeakVolt.StartDate, ValleyVolt.StartDate };
        var dateStart = dates.Min(v => v).AddMinutes(-1);
        var dateEnd = dates.Max(v => v);
        var dateStartDateEnd = new Func<FXW.Rate, bool>(t => t.StartDate.Between(dateStart, dateEnd));
        tickHigh = ticks.Where(dateStartDateEnd).OrderBy(t => t.BidHigh).Last();
        PeakPriceHigh = res.TradeStats.peakPriceHigh = tickHigh.BidHigh;
        PeakPriceHighAverage = res.TradeStats.peakPriceHighAvg =
          Math.Round(ticks.Where(t => agvRange(t, tickHigh)).Average(t => t.BidHigh), fw.Digits);

        tickLow = ticks.Where(dateStartDateEnd).OrderBy(t => t.AskLow).First();
        ValleyPriceLow = res.TradeStats.valleyPriceLow = tickLow.AskLow;
        ValleyPriceLowAverage = res.TradeStats.valleyPriceLowAvg =
          Math.Round(ticks.Where(t => agvRange(t, tickLow)).Average(t => t.AskLow), fw.Digits);
      }

    }
    #endregion

    #region ProcessPrice
    O2G.Price priceCurrent;
    FXW.Rate lastBar = new Order2GoAddIn.FXCoreWrapper.Rate();
    void ProcessPrice(Order2GoAddIn.Price price, ref List<FXW.Rate> ticks) {
      try {
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

        TicksPerMinuteAverageLong = (int)(ticks.Count / ticks.Last().StartDate.Subtract(ticks.First().StartDate).TotalMinutes);
        #endregion

        #region Do Price
        if (price != null && !TestMode) {
          priceCurrent = price;
          var priceTime = price.Time > ServerTime.AddMinutes(10) ? price.Time.AddHours(-1) : price.Time;
          RunMinutesBack(price);
          if (doTicks) {
            ticks.Add(new FXW.Rate(priceTime, price.Ask, price.Bid, false));
            //Ticks.FillRSI(14, getPrice, getRsi, setRsi);
            GetTicksAsync(ticksStartDate, fxDateNow);
          } else {
            var lastTickTime = ServerTime.Round().AddMinutes(-1);
            if ((lastTickTime - _ticks.Last().StartDate).TotalMilliseconds > 0)
              GetTicksAsync(ticksStartDate, lastTickTime);
            if (lastBar.StartDate.AddMinutes(1) < ServerTime)
              lastBar = new FXCoreWrapper.Rate(false);
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
    void RunDecider(Price price) {
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
          var wave1 = waves.OrderByDescending(w => w.Date).FirstOrDefault() ?? new Signaler.DataPoint();
          var wave2 = waves.OrderByDescending(w => w.Date).Skip(1).FirstOrDefault() ?? wave1;
          CorridorsWindow_EURJPY.AddTicks(
            null,
            TicksInTimeFrame.ToList(),
            Voltages,
            PeakVolt.AverageAsk,
            ValleyVolt.AverageBid, 0, 0, 0, 0,
            wave1.Date, wave2.Date,
            //PeakVolt.StartDate, ValleyVolt.StartDate, 
            new[] { 0.0 });
        };
    }
    #endregion

    #region Decisioner

    public TradeStatistics GetTradingStatistics(TradeRequest tradeRequest, TradeStatistics tradeStats, double positionBuy, double positionSell) {
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
      RidOfOldPositions(tr, buySellSignals[tr]);
      var isReady = !MinutesBackScheduler.IsRunning;
      var trKey = buySellSignals.Keys.Where(k => k == tr).First();
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
      response.IsReady = isReady;
      return response;
    }
    public void Decisioner(Order2GoAddIn.Price eventPrice, TradeRequest tr, TradeResponse ti) {
      try {
        var logHeader = "D"; var dateNow = DateTime.Now; Func<string, string> timeSpan = step => logHeader + " : " + (DateTime.Now - dateNow).TotalMilliseconds + " - " + step;
        if (fw == null || fw.Desk == null || TestMode) return;
        VLog = timeSpan("Start");
        var ticksInTimeFrame = TicksInTimeFrame;
        VLog = timeSpan("Ticks");


        if (ticksInTimeFrame.Length == 0 || MinutesBackSampleCount == 0) {
          ti.CorridorOK = false;
          IsBuyMode = null;
          return;
        }
        ShowTicks();

        var price = eventPrice ?? priceCurrent;

        #region Buy/Sell position
        bool canBuy = false, canSell = false;

        if (!tr.doTrend) GetExtreamPrices(tr, ti);
        Func<double, double> volatilityLambda = v => tr.tradeByVolatilityMaximum.HasValue ? tr.tradeByVolatilityMaximum.Value ? VolatilityMaxInPips : VolatilityAvgInPips : v;
        var positionBuy = !tr.doTrend ? fw.InPips(VolatilityUp - price.Ask, 1) :
         -PriceHeightInPips - volatilityLambda(VolatilityDownInPips);

        var positionSell = !tr.doTrend ? fw.InPips(price.Bid - ti.TradeStats.peakPriceHigh, 1) :
          PriceHeightInPips - volatilityLambda(VolatilityUpInPips);
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
          return fw.InPips(VolatilityUpPeak + VolatilityDownPeak, 1);
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
        var goTradeFoos = new[] { goTrade_0, goTrade_1, goTrade_2, goTrade_3, goTrade_4, goTrade_5, goTrade_6 };
        #endregion

        bool goBuy = false, goSell = false;
        #region Action - decideByVoltage_11
        Action decideByVoltage_11 = () => {
          //var ticksRsi = ticksInTimeFrame.GetMinuteTicks(tr.rsiBar).ToList();
          //var rsiBuy = Indicators.RSI_CR(ticksRsi, r => r.AskLow, tr.rsiPeriod);
          //var rsiSell = Indicators.RSI_CR(ticksRsi, r => r.BidHigh, tr.rsiPeriod);

          var ratesForRsi = Ticks.Where(t=>t.StartDate>=ServerTime.AddMinutes(-16)).GetMinuteTicks(1);
          ratesForRsi.FillRSI(14,r=>r.PriceClose, (r,d) => r.PriceRsi = d);
          if (!ratesForRsi.Any(r => r.PriceRsi.HasValue)) return;
          ratesForRsi.Where(r=>r.PriceRsi.HasValue).ToArray().CR(r => (double)r.PriceRsi, (FXW.Rate r, double d) => r.PriceRsiCR = d);
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
          var rsiCanBuy = !tr.tradeByRsi || ti.RsiLow.Between(0.01, ti.RsiRegressionOffsetBuy*1.15);// && lastFractalBuy != null && lastFractalBuy.StartDate > ServerTime.AddMinutes(-5);
          var rsiCanSell = !tr.tradeByRsi || ti.RsiHigh >= ti.RsiRegressionOffsetSell * .85;// && lastFractalSell != null && lastFractalSell.StartDate > ServerTime.AddMinutes(-5); ;
          //Func<double, bool> closeToExtreamLambda = pe => Math.Abs(pe - readFrom(Ticks.Last())) < price.Spread;
          //fractalLast = ratesForRsi.HasFractal(true).Last();
          var fractalRange = SpreadAverage * tr.tradeByFractalCoeff;
          var fractalBuy = FractalBuy.StartDate > FractalSell.StartDate;
          var fractalSell = FractalSell.StartDate > FractalBuy.StartDate;
          IsBuyMode = !fractalBuy && !fractalSell ? (bool?)null : fractalBuy;

          fractalBuy = FractalBuyColor = fractalBuy && price.Ask.Between(FractalBuy.AskLow, FractalBuy.AskLow + fractalRange);
          fractalSell = FractalSellColor = fractalSell && price.Bid.Between(FractalSell.BidHigh - fractalRange, FractalSell.BidHigh);

          var angleCanBuy = Angle.Between(-tr.tradeAngleMax,-tr.tradeAngleMin);
          var angleCanSell = Angle.Between(tr.tradeAngleMin, tr.tradeAngleMax);

          canBuy = goTradeFoos[tr.goTradeFooBuy](positionBuy, true);// closeToExtreamLambda(priceHeightMin);
          goBuy = canBuy && rsiCanBuy && fractalBuy && angleCanBuy && (tr.doTrend || ti.TradeStats.valleyVolts >= ti.TradeStats.voltsAverage);

          canSell = goTradeFoos[tr.goTradeFooSell](positionSell, false);// closeToExtreamLambda(priceHeightMax);
          goSell = canSell && rsiCanSell && fractalSell && angleCanSell && (tr.doTrend || ti.TradeStats.peakVolts >= ti.TradeStats.voltsAverage);

          ti.DencityRatio = densityFoos[tr.densityFoo](goBuy);
          ti.DencityRatioBuy = densityFoos[tr.densityFoo](true);
          ti.DencityRatioSell = densityFoos[tr.densityFoo](false);

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
        var ticksForDelay = ticksInTimeFrame.Skip(ticksInTimeFrame.Length-100).ToArray();
        ti.TradeSignalDelay = Math.Ceiling(((ticksForDelay.Last().StartDate - ticksForDelay.First().StartDate).TotalSeconds / 100)).ToInt() * 2;
        if (goBuy) ti.GoBuyTime = ServerTime.AddSeconds(isDirectionOk(true) ? 0 : 1);
        if (goSell) ti.GoSellTime = ServerTime.AddSeconds(isDirectionOk(false) ? 0 : 1);
        if (goSell || goBuy) ti.TradeWaveInMinutes = TimeframeInMinutes;
        #endregion

        #region Corridor
        var corridorMinimum = SpreadByBarPeriod(tr.corridorMinites, false);
        if (CorridorSpread(tr.doTrend) < corridorMinimum) {
          ti.CorridorOK = false;
          ti.GoBuyTime = ti.GoSellTime = DateTime.MinValue;
        } else ti.CorridorOK = true;
        #endregion

        VLog = timeSpan("**End**");

      } catch (Exception exc) {
        Log = exc;
      }
    }
    void RidOfOldPositions(TradeRequest tr, TradeResponse ti) {
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
      var lotsToTrade_3 = new Func<bool, int>((buy) => 1);
      var lotsToTradeFoos = new[] { lotsToTrade_0, lotsToTrade_1, lotsToTrade_2, lotsToTrade_3 };
      #endregion
      ti.LotsToTradeBuy = lotsToTradeFoos[tr.lotsToTradeFoo](true);
      ti.LotsToTradeSell = lotsToTradeFoos[tr.lotsToTradeFoo](false);

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
      Func<Trade[],int, double> profitMin = (trades,count) => {
        if (trades.Length < 2) return 0;
        var lastTwo = trades.OrderByDescending(t => t.PL).Take(count).ToArray();
        return Math.Abs(lastTwo.Max(t => t.PL) - lastTwo.Min(t => t.PL)) * tr.profitMin / 10;
      };

      #region Trade Added
      if (tr.tradeAdded != null) {
        var leaveOpenAfterTruncate = 0;
        while (tr.tradeAdded.Buy && tr.SellPositions > 0) {
          var plMin = profitMin(tr.tradesSell,3);
          if (tr.SellNetPLPip >= plMin) {
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
          var plMin = profitMin(tr.tradesBuy,3);
          if (tr.BuyNetPLPip >= plMin) {
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
      if (tr.closeOnNet) {
        Func<Trade[], double> plNetMin = trades => trades.Length == 0 ? 0 : CorridorSpreadInPips / (trades.Sum(t => t.Lots) / trades.Min(t => t.Lots));
        var plNetMinBuy = plNetMin(tr.tradesBuy);
        if (tr.BuyNetPLPip >= plNetMinBuy) closeBuyIDs.AddRange(tr.tradesBuy.Select(t => t.Id));
        var plNetMinSell = plNetMin(tr.tradesSell);
        if (tr.SellNetPLPip >= plNetMinSell) closeSellIDs.AddRange(tr.tradesSell.Select(t => t.Id));
      }
      #endregion

      #region Colose on Corridor
      if (tr.closeOnCorridorBorder.HasValue) {
        if (tr.closeOnCorridorBorder.Value && priceCurrent != null) {
          Func<Trade, FXW.Rate[]> ticksLamda = trade => TicksInTimeFrame.Where(t => t.StartDate >= trade.Time).ToArray();
          closeBuyIDs.AddRange(
            tr.tradesBuy.Where(
            t => t.PL >= (CorridorSpreadInPips / 2) && (priceCurrent.Average - ticksLamda(t).Min(tick => tick.PriceAvg)) >= CorridorSpread(true)
            ).Select(t => t.Id));
          closeSellIDs.AddRange(
            tr.tradesSell.Where(
            t => t.PL >= (CorridorSpreadInPips / 2) && (-priceCurrent.Average + ticksLamda(t).Max(tick => tick.PriceAvg)) >= CorridorSpread(true)
            ).Select(t => t.Id));
        } else {
          closeBuyIDs.AddRange(tr.tradesBuy.Where(t => t.PL >= CorridorSpreadInPips/* t.Remark.TradeWaveHeight*/).Select(t => t.Id));
          closeSellIDs.AddRange(tr.tradesSell.Where(t => t.PL >= CorridorSpreadInPips/* t.Remark.TradeWaveHeight*/).Select(t => t.Id));
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
          var plMin = profitMin(tr.tradesBuy,2);
          var closeBuys = tr.tradesBuy.Length > 1 ? tr.tradesBuy.Where(t => t.PL >= plMin).ToArray() : new Trade[] { };
          if (closeBuys.Length >= Math.Min(tr.tradesBuy.Length, tr.closeIfProfitTradesMoreThen + 1)) {
            var tc = tradesCount(closeBuys);
            closeBuyIDs.AddRange(closeBuys.OrderByDescending(t => t.GrossPL).Take(tc).Select(t => t.Id));
          }
        }
        {
          var plMin = profitMin(tr.tradesSell,2);
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
