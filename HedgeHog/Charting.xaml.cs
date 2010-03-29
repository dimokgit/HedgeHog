using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using HedgeHog.Bars;
using Order2GoAddIn;
using FXW = Order2GoAddIn.FXCoreWrapper;
namespace HedgeHog {

  public partial class Charting : Window, INotifyPropertyChanged {

    #region Data Stores
    public class PriceBar {
      [DisplayName("")]
      public double AskHigh { get; set; }
      [DisplayName("")]
      public double AskLow { get; set; }
      [DisplayName("")]
      public double BidLow { get; set; }
      [DisplayName("")]
      public double BidHigh { get; set; }
      [DisplayName("Avg")]
      public double Average { get { return (AverageAsk + AverageBid) / 2; } }
      [DisplayName("")]
      public double AverageAsk { get; set; }
      [DisplayName("")]
      public double AverageBid { get; set; }
      [DisplayFormat(DataFormatString = "{0:n0}")]
      [DisplayName("")]
      public double Spread { get; set; }
      [DisplayFormat(DataFormatString = "{0:n1}")]
      [DisplayName("")]
      public double Speed { get; set; }
      [DisplayName("")]
      [DisplayFormat(DataFormatString = "{0:n1}")]
      public double Distance { get; set; }
      [DisplayFormat(DataFormatString = "{0:n3}")]
      [DisplayName("Volts")]
      public double Volts { get { return Distance / Row; } }
      [DisplayFormat(DataFormatString = "{0:n1}")]
      [DisplayName("Row")]
      public double Row { get; set; }
      [DisplayFormat(DataFormatString = "{0:dd HH:mm}")]
      [DisplayName("Date")]
      public DateTime StartDate { get; set; }
      [DisplayName("")]
      [DisplayFormat(DataFormatString = "{0:n1}")]
      public double Power { get { return Spread * Speed; } }
    }

    #endregion

    #region Properties
    System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer();
    List<Tick> Ticks = null;
    private ViewModel DC { get { return DataContext as ViewModel; } }
    VoltForGrid PeakVolt = null;
    VoltForGrid ValleyVolt = null;
    List<Volt> VoltagesByTick = new List<Volt>();

    public bool GoBuy { get; protected set; }
    public bool GoSell { get; protected set; }
    public bool CloseBuy { get; protected set; }
    public bool CloseSell { get; protected set; }
    public double StopLossBuy { get; protected set; }
    public double StopLossSell { get; protected set; }
    public double TakeProfitBuy { get; protected set; }
    public double TakeProfitSell { get; protected set; }
    public int LotsToTradeBuy = 1;
    public int LotsToTradeSell = 1;
    public Order2GoAddIn.TradeRemark TradeInfo { get; protected set; }
    public double DencityRatio { get; protected set; }

    public int _txtVoltageCMA;
    private int voltageCMA { get { return _txtVoltageCMA; } }

    public int bsPeriodMin { get { return 1; } }

    public int _txtCloseOppositeOffset;
    private int closeOppositeOffset { get { return _txtCloseOppositeOffset; } }

    public double _txtProfitMin;
    private double profitMin { get { return _txtProfitMin; } }

    public string _txtServerName;
    private string serverName { get { return _txtServerName; } }

    public string _txtServerPort;
    private string serverPort { 
      get { return _txtServerPort; } 
      set { _txtServerPort = value; } 
    }

    public int _txtCorridorMinMinuteBar;
    private int corridorMinMinuteBar { get { return _txtCorridorMinMinuteBar; } }

    public int _txtRSITradeSignalPeriod;
    public int RsiTradeSignalPeriod { get { return _txtRSITradeSignalPeriod; } }

    public int _txtTradeOnProfitAfter;
    public int TradeOnProfitAfter { get { return _txtTradeOnProfitAfter; } }

    public double _txtTradeAngleMax;
    public double TradeAngleMax { get { return _txtTradeAngleMax; } }

    public double _txtTradeAngleMin;
    public double TradeAngleMin { get { return _txtTradeAngleMin; } }

    public double _txtTradeByFractalCoeff;
    public double TradeByFractalCoeff { get { return _txtTradeByFractalCoeff; } }


    public int _txtRSITradeSignalBar;
    public int RsiTradeSignalBar { get { return _txtRSITradeSignalBar; } }

    public int _txtRSITradeSignalTresholdBuy;
    public int RsiTradeSignalTresholdBuy { get { return _txtRSITradeSignalTresholdBuy; } }

    public int _txtRSITradeSignalTresholdSell;
    public int RsiTradeSignalTresholdSell { get { return _txtRSITradeSignalTresholdSell; } }

    public int _txtRSIProfit;
    public int RsiProfit { get { return _txtRSIProfit; } }

    public string tcpPath {
      get {
        return "tcp://" + serverName + ":" + serverPort + "/Get";
      }
    }

    public int _txtShortStack;
    private int shortStack { get { return _txtShortStack; } }

    public double _txtCloseTradeFibRatio;
    private double closeTradeFibRatio { get { return _txtCloseTradeFibRatio; } }

    public int _txtShortStackTruncateOffset;
    private int shortStackTruncateOffset { get { return _txtShortStackTruncateOffset; } }

    public int _txtCorridorSmoothSeconds;
    private int corridorSmoothSeconds { get { return _txtCorridorSmoothSeconds; } }

    public bool _chkCloseOnNet;
    private bool closeOnNet { get { return _chkCloseOnNet; } }

    public bool _chkCloseAllOnTrade;
    private bool closeAllOnTrade { get { return _chkCloseAllOnTrade; } }
    
    private bool sellOnProfitLast { get { return Lib.GetChecked(chkSellOnProfitLast).Value; } }
    private bool closeOnProfitOnly { get { return Lib.GetChecked(chkCloseOnProfitOnly).Value; } }
    private bool doTrend { get { return Lib.GetChecked(chkDoTrend).Value; } }
    private bool tradeByDirection { get { return Lib.GetChecked(chkTradeByDirection).Value; } }
    private bool setLimitOrder { get { return Lib.GetChecked(chkSetLimitOrder).Value; } }
    private bool doBiDirection { get { return Lib.GetChecked(chkDoBiDirection).Value; } }
    private bool moveTimeFrameByPos { get { return false; } }

    public bool? _chkTradeByVolatilityMax;
    public bool? tradeByVolatilityMax {
      get { return _chkTradeByVolatilityMax.HasValue ? _chkTradeByVolatilityMax : null; }
      set { _chkTradeByVolatilityMax = value.HasValue ? value : null; }
    }
    private void chkTradeByVolatilityMax_Loaded(object sender, RoutedEventArgs e) {
      DependencyPropertyDescriptor.FromProperty(CheckBox.IsCheckedProperty, this.GetType()).AddValueChanged(chkTradeByVolatilityMax, (s, re) => {
        tradeByVolatilityMax = chkTradeByVolatilityMax.IsChecked;
      });
    }


    public bool _chkRSIUseOffset;
    public bool? RsiUseOffset { get { return _chkRSIUseOffset; } }

    public bool _chkTradeByRsi;
    private bool tradeByRsi { get { return _chkTradeByRsi; } }

    public bool? _chkCloseOnCorridorBorder;
    public bool? CloseOnCorridorBorder {
      get { return _chkCloseOnCorridorBorder.HasValue ? _chkCloseOnCorridorBorder : null; }
      set { _chkCloseOnCorridorBorder = value.HasValue ? value : null; }
    }
    private void chkCloseOnCorridorBorder_Loaded(object sender, RoutedEventArgs e) {
      CloseOnCorridorBorder = chkCloseOnCorridorBorder.IsChecked;
      DependencyPropertyDescriptor.FromProperty(CheckBox.IsCheckedProperty, this.GetType()).AddValueChanged(chkCloseOnCorridorBorder, (s, re) => {
        CloseOnCorridorBorder = chkCloseOnCorridorBorder.IsChecked;
      });
    }



    public int _txtFoo;
    private int fooNumber { get { return _txtFoo; } }

    private int fooPositionBuy { get { return Lib.GetComboBoxIndex(cbPositionFooBuy); } }
    private int fooPositionSell { get { return Lib.GetComboBoxIndex(cbPositionFooSell); } }

    private int fooDensity { get { return Lib.GetComboBoxIndex(cbDensityFoo); } }
    private int fooGoTradeBuy { get { return Lib.GetComboBoxIndex(cbGoTradeBuy); } }
    private int fooGoTradeSell { get { return Lib.GetComboBoxIndex(cbGoTradeSell); } }

    public int _txtHighMinutes;
    private int highMinutes { get { return _txtHighMinutes; } }

    public int _txtCloseIfProfitTradesMoreThen;
    private int closeIfProfitTradesMoreThen { get { return _txtCloseIfProfitTradesMoreThen; } }

    public int _txtCloseProfitTradesMaximum;
    private int closeProfitTradesMaximum { get { return _txtCloseProfitTradesMaximum; } }

    private bool forceOpenTradeBuy { get { return Lib.GetChecked(chkOpenTradeBuy).Value; } set { Lib.SetChecked(chkOpenTradeBuy, value, true); } }
    private bool forceOpenTradeSell { get { return Lib.GetChecked(chkOpenTradeSell).Value; } set { Lib.SetChecked(chkOpenTradeSell, value, true); } }

    private double spreadAverage = 0;
    private double spreadAverageInPips { get { return fw.InPips(spreadAverage, 1); } }
    private double spreadAverage5Min = 0;
    private double spreadAverage5MinInPips { get { return fw.InPips(spreadAverage5Min, 1); } }
    private double spreadAverage10Min = 0;
    private double spreadAverage10MinInPips { get { return fw.InPips(spreadAverage10Min, 1); } }
    private double spreadAverage15Min = 0;
    private double spreadAverage15MinInPips { get { return fw.InPips(spreadAverage15Min, 1); } }
    private double spreadAverageHighMin = 0;
    private double spreadAverageHighMinInPips { get { return fw.InPips(spreadAverageHighMin, 1); } }
    private Order2GoAddIn.Trade tradeAdded = null;

    private bool _canTrade;
    public bool CanTrade {
      get { return _canTrade; }
      set {
        _canTrade = value;
        if (PropertyChanged != null)
          PropertyChanged(this, new PropertyChangedEventArgs("CanTrade"));
      }
    }

    Order2GoAddIn.FXCoreWrapper fw { get; set; }
    string pair { get { return fw.Pair; } }
    #endregion

    #region Events
    public delegate void PriceGridErrorHandler(Exception exc);
    public event PriceGridErrorHandler PriceGridError;
    protected void RaisePriceGridError(Exception exc){      if( PriceGridError!= null)PriceGridError(exc);}
    public class TickChangedEventArgs : EventArgs {
      public List<Rate> Ticks;
      public double VoltageHigh { get; set; }
      public double VoltageCurr { get; set; }
      public double NetBuy { get; set; }
      public double NetSell { get; set; }
      public double[] PriceAverage { get; set; }
      public double PriceMaxAverage { get; set; }
      public double PriceMinAverage { get; set; }
      public DateTime TimeHigh { get; set; }
      public DateTime TimeCurr { get; set; }
      public List<Volt> VoltsByTick { get; set; }
      public TickChangedEventArgs(List<Rate> ticks,
        double voltageHigh, double voltageCurr, double netBuy, double netSell, DateTime timeHigh, DateTime timeCurr) :
        this(ticks, voltageHigh, voltageCurr, 0, 0, netBuy, netSell, timeHigh, timeCurr, new[] { 0.0, 0.0 }, null) {
      }
      public TickChangedEventArgs(List<Rate> ticks,
        double voltageHigh, double voltageCurr, double priceMaxAverage, double priceMinAverage,
        double netBuy, double netSell, DateTime timeHigh, DateTime timeCurr, double[] priceAverage, List<Volt>  voltsByTick) {
        Ticks = ticks;
        VoltageHigh = voltageHigh;
        VoltageCurr = voltageCurr;
        PriceMaxAverage = priceMaxAverage;
        PriceMinAverage = priceMinAverage;
        NetBuy= netBuy;
        NetSell = netSell;
        TimeHigh = timeHigh;
        TimeCurr = timeCurr;
        PriceAverage = priceAverage;
        VoltsByTick = voltsByTick;
      }
    }
    public event EventHandler<TickChangedEventArgs> TicksChanged;

    void OnTicksChanged(List<Rate> ticks, VoltForGrid voltageHight, VoltForGrid voltageCurr, double netBuy, double netSell) {
      OnTicksChanged(ticks,
        voltageHight != null ? voltageHight.Average : 0, voltageCurr != null ? voltageCurr.Average : 0, netBuy, netSell,
        voltageHight != null ? voltageHight.StartDate : DateTime.MinValue, voltageCurr != null ? voltageCurr.StartDate : DateTime.MinValue);
    }
    void OnTicksChanged(List<Rate> ticks, double voltageHight, double voltageCurr, 
      double netBuy,double netSell, DateTime timeHigh, DateTime timeCurr) {
      OnTicksChanged(ticks, voltageHight,0,0, voltageCurr, netBuy,netSell, timeHigh, timeCurr, new[]{0.0,0.0},null);
    }
    void OnTicksChanged(List<Rate> ticks, double voltageHight, double voltageCurr,
      double priceMaxAverage, List<Volt> voltsByTick) {
      OnTicksChanged(ticks, voltageHight, voltageCurr, priceMaxAverage, 0, 0, 0, DateTime.Now, DateTime.Now, new double[] { }, voltsByTick);
    }
    void OnTicksChanged(List<Rate> ticks, double voltageHight, double voltageCurr,
      double priceMaxAverage,double priceMinAverage,      double netBuy,double netSell,
      DateTime timeHigh, DateTime timeCurr, double[] priceAverage, List<Volt> voltsByTick) {
      if (TicksChanged != null) TicksChanged(this, 
        new TickChangedEventArgs(ticks, voltageHight, voltageCurr,priceMaxAverage,priceMinAverage, netBuy,netSell,timeHigh,timeCurr,priceAverage,voltsByTick));
    }

    public event Action PriceGridChanged;
    #endregion

    #region Ctor
    Scheduler showBarsScheduler;
    Scheduler showCorridorScheduler;
    ThreadScheduler getVoltagesScheduler;
    ThreadScheduler ClosePositionsScheduler;
    ThreadScheduler RsiScheduler;
    public Charting() :this("",null){
    }
    public Charting(string name, Order2GoAddIn.FXCoreWrapper FW) {
      this.Name = name;
      InitializeComponent();
      if (FW != null) {
        //timer.Interval = new TimeSpan(0, 1, 0);
        //timer.Tick += new EventHandler(timer_Tick);
        //timer.Start();
        showBarsScheduler = new Scheduler(Dispatcher);
        showCorridorScheduler = new Scheduler(Dispatcher);
        getVoltagesScheduler = new ThreadScheduler(new TimeSpan(0, 0, 0));
        //RsiScheduler = new ThreadScheduler(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10),
        //  () => GetRsiRates(), (s, e) => RaisePriceGridError(e.Exception));
        ClosePositionsScheduler = new ThreadScheduler(TimeSpan.FromMilliseconds(1), (s, e) => RaisePriceGridError(e.Exception));
        fw = FW;
        fw.PriceChanged += new Order2GoAddIn.FXCoreWrapper.PriceChangedEventHandler(fw_PriceChanged);
        PriceScheduler = new ThreadScheduler(
          TimeSpan.FromSeconds(1), ThreadScheduler.infinity, ProcessPrice, (s, e) => RaisePriceGridError(e.Exception));
      }
    }
    List<Rate> rsiBars = new List<Rate>();
    void ProcessRsi() {
      fw.GetBars(5, fw.ServerTime.Round(5).AddHours(-12), DateTime.FromOADate(0), ref rsiBars);
    }
    void timer_Tick(object sender, EventArgs e) {
      try {
        if (fw.ServerTime.AddMinutes(2) > DateTime.Now) return;
      } catch { }
      try {
        fw.LogOn();
      } catch (Exception exc) {
        if (PriceGridError != null) PriceGridError(exc);
        Thread.Sleep(1000);
      }
    }
    #endregion

    #region Run Price
    bool runPrice = false;
    IAsyncResult asyncRes = null;
    object priceSync = new object();
    ThreadScheduler PriceScheduler;
    public void fw_PriceChanged() { fw_PriceChanged(null); }
    void fw_PriceChanged(Order2GoAddIn.Price Price) {
      lock (priceSync) {
        try {
          PriceScheduler.Cancel();
          ProcessPrice(Price);
        } catch (Exception exc) {
          RaisePriceGridError(exc);
        }
      }
      return;
      runPrice = true;
      if (asyncRes == null || asyncRes.IsCompleted) {
        var d = new Action<Order2GoAddIn.Price>(ProcessPrice);
        asyncRes = d.BeginInvoke(Price, priceCallBack, d);
        runPrice = false;
      }
    }
    void priceCallBack(IAsyncResult res) {
      if (res != null && res.AsyncState != null)
        ((Action)res.AsyncState).EndInvoke(res);
      if (runPrice) fw_PriceChanged(null);
    }
    #endregion

    List<Rate> rsiRates = new List<Rate>();
    void GetRsiRates() {
      //if (RsiScheduler.IsRunning) { RaisePriceGridError(new Exception("RsiScheduler is overwelmed.")); return; }
      var startTime = fw.ServerTime.AddHours(-12).Round(1);
      var endTime = DateTime.FromOADate(0);
      fw.GetBars(1, startTime, endTime, ref rsiRates);
      rsiRates.Where(r => r.StartDate < startTime).ToList().ForEach(r => rsiRates.Remove(r));
      rsiRates.Remove(rsiRates.Last());
      var rsiChart = rsiRates.GetMinuteTicks(RsiTradeSignalBar);
      var rsi = Order2GoAddIn.Indicators.RSI(rsiChart, r => r.PriceAvg, RsiTradeSignalPeriod);
      var rsiVolts = rsi.Select(r => new Volt() { StartDate = r.Time, Volts = r.Point == 0 ? 50 : r.Point }).ToList();
      //OnTicksChanged(rsiChart.ToList(),/*RsiTradeSignalTresholdBuy+*/RsiBuy,/*RsiTradeSignalTresholdSell+*/RsiSell,RsiAverage, rsiVolts);
    }

    #region ProcessPrice
    bool IsSecondTrade(Order2GoAddIn.Trade trade) {
      return trade!=null && fw.GetTrades(!trade.Buy).Any(t => (trade.Time - t.Time).TotalSeconds.Between(0, 15));
    }
    bool isNewTradeInititalized = false;
    List<string> closeTradeIDs = new List<string>();
    void CloseTrades() {
      if (!ClosePositionsScheduler.IsRunning)
        ClosePositionsScheduler.Command = () => {
          var ct = from t in fw.GetTrades()
                   join c in closeTradeIDs on t.Id equals c
                   select c;
          ct.ToList().ForEach(t => fw.FixOrderClose(t));
        };
    }
    bool doSecondTrade = false;
    [MethodImpl(MethodImplOptions.Synchronized)]
    public void ProcessPrice() {
      ProcessPrice(null);
    }

    int _rsiRegressionOffsetBuy = 0;
    public int RsiBuy {
      get { return _rsiRegressionOffsetBuy; }
      set { _rsiRegressionOffsetBuy = value; }
    }
    int _rsiRegressionOffsetSell = 0;
    public int RsiSell {
      get { return _rsiRegressionOffsetSell; }
      set { _rsiRegressionOffsetSell = value; }
    }

    int _rsiAverage;
    public int RsiAverage {
      get { return _rsiAverage; }
      set { _rsiAverage = value; }
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void ProcessPrice( Order2GoAddIn.Price eventPrice) {
      try {
        if (fw == null || fw.Desk == null) return;
        #region Local Globals
        int periodMin = bsPeriodMin;
        Order2GoAddIn.Summary summary = fw.GetSummary();
        Order2GoAddIn.Account account = fw.GetAccount();
        var price = eventPrice ?? summary.PriceCurrent;
        #endregion

        #region Global Globals
        GoSell = GoBuy = CloseSell = CloseBuy = false;
        TakeProfitBuy = TakeProfitSell = StopLossSell = 0;
        LotsToTradeBuy = LotsToTradeSell = 1;
        DencityRatio = 1;
        #endregion

        var timeNow = DateTime.Now;
        DateTime serverTime = fw.ServerTime;

        #region Buy/Sell Signals
        this.CanTrade = true;
        bool goBuy = false, goSell = false;
        double positionBuy = 0, positionSell = 0;

        var voltPriceMax = PeakVolt == null ? 0 : PeakVolt.AverageAsk;
        var voltPriceMin = ValleyVolt == null ? 0 : ValleyVolt.AverageBid;

        var corridorOK = false;
        var corridorMinimum = 100.0;
        int ticksPerMinuteAverageShort = 0;
        int ticksPerMinuteAverageLong = 0;

        #region Deciders 11

        Action decideByVoltage_11 = () => {
          #region Rid of Old Positions
          if (!isNewTradeInititalized) {
            isNewTradeInititalized = true;
            fw.TradesCountChanged += (trade) => {
              if (IsSecondTrade(trade)) return;
              tradeAdded = trade;
              forceOpenTradeBuy = forceOpenTradeSell = false;
              doSecondTrade = !IsSecondTrade(tradeAdded);
              //System.IO.File.AppendAllText("Trades.xml", trade.ToString() + Environment.NewLine);
            };
          }
          #endregion

          #region TradeRequest
          var tr = new TradeRequest() {
            tradeAdded = tradeAdded,
            BuyNetPLPip = summary.BuyNetPLPip,
            BuyPositions = (int)summary.BuyPositions,
            SellNetPLPip = summary.SellNetPLPip,
            SellPositions = (int)summary.SellPositions,
            closeOppositeOffset = this.closeOppositeOffset,
            corridorMinites = this.corridorMinMinuteBar,
            DecisionFoo = this.fooNumber,
            densityFoo = this.fooDensity,
            goTradeFooBuy = this.fooGoTradeBuy,
            goTradeFooSell = this.fooGoTradeSell,
            highBarMinutes = this.highMinutes,
            lotsToTradeFooBuy = this.fooPositionBuy,
            lotsToTradeFooSell = this.fooPositionSell,
            profitMin = this.profitMin,
            sellOnProfitLast = this.sellOnProfitLast,
            closeOnProfitOnly = this.closeOnProfitOnly,
            closeAllOnTrade = this.closeAllOnTrade,
            shortStack = this.shortStack,
            shortStackTruncateOffset = this.shortStackTruncateOffset,
            corridorSmoothSeconds = this.corridorSmoothSeconds,
            doTrend = this.doTrend,
            tradeByDirection = this.tradeByDirection,
            setLimitOrder = this.setLimitOrder,
            closeTradeFibRatio = this.closeTradeFibRatio,
            tradesBuy = fw.GetTrades(true).ToArray(),
            tradesSell = fw.GetTrades(false).ToArray(),
            closeIfProfitTradesMoreThen = this.closeIfProfitTradesMoreThen,
            closeProfitTradesMaximum = this.closeProfitTradesMaximum,
            tradeByVolatilityMaximum = this.tradeByVolatilityMax,
            rsiPeriod = RsiTradeSignalPeriod,
            rsiBar = RsiTradeSignalBar,
            rsiTresholdBuy = RsiTradeSignalTresholdBuy,
            rsiTresholdSell = RsiTradeSignalTresholdSell,
            rsiProfit = this.RsiProfit,
            tradeByRsi = this.tradeByRsi,
            closeOnCorridorBorder = this.CloseOnCorridorBorder,
            closeOnNet = this.closeOnNet,
            tradeOnProfitAfter = this.TradeOnProfitAfter,
            tradeAngleMax = this.TradeAngleMax,
            tradeAngleMin = this.TradeAngleMin,
            tradeByFractalCoeff = this.TradeByFractalCoeff,
            serverTime = serverTime
          };
          tradeAdded = null;
          #endregion

          DC.Trades.Clear();
          tr.tradesBuy.ToList().ForEach(t => DC.Trades.Add(t));
          tr.tradesSell.ToList().ForEach(t => DC.Trades.Add(t));

          TradeResponse ti = null;
          for (int sp = int.Parse(serverPort) - 5, spMax = sp + 10; sp < spMax; sp++) 
            try {
              var rc = RemoteClient.Activate(tcpPath);
              ti = rc.Decisioner(tr);
              break;
            } catch (System.Net.Sockets.SocketException) {
              RaisePriceGridError(new Exception(tcpPath + " not found."));
              return;
              serverPort = sp + "";
            }
          if (ti == null) throw new Exception(tcpPath + " not found.");

          //if (!ti.IsReady) PriceScheduler.Run();

          #region Statts
          var ts = ti.TradeStats;
          if (ts == null) return;
          TradeInfo = new Order2GoAddIn.TradeRemark(ti.TradeWaveInMinutes, fw.InPips(ti.TradeStats.corridorSpread, 1), ti.TradeStats.Angle / fw.PointSize);
          positionBuy = ts.positionBuy;
          positionSell = ts.positionSell;
          spreadAverage = ts.spreadAverage;
          spreadAverage5Min = ts.spreadAverage5Min;
          spreadAverage10Min = ts.spreadAverage10Min;
          spreadAverage15Min = ts.spreadAverage15Min;
          spreadAverageHighMin = ts.spreadAverageHighMin;
          voltPriceMax = ts.voltPriceMax;
          voltPriceMin = ts.voltPriceMin;
          ticksPerMinuteAverageShort = (int)ts.ticksPerMinuteAverageShort;
          ticksPerMinuteAverageLong = (int)ts.ticksPerMinuteAverageLong;
          corridorMinimum = ts.corridorMinimum;
          closeTradeIDs.AddRange(ti.CloseTradeIDs);
          CloseTrades();
          Dispatcher.BeginInvoke(new Action(() => {
            DC.TimeFrame = ts.timeFrame;
            DC.CorridorMinimum = fw.InPips(corridorMinimum, 1);
            DC.CorridorMinimumToTrade = spreadAverageHighMinInPips;
            DC.NextTradeDensityBuy = ti.DencityRatioBuy;
            DC.NextTradeDensitySell = ti.DencityRatioSell;
            DC.CorridorSpread = fw.InPips(ti.TradeStats.corridorSpread, 1);
            DC.RsiHigh = ti.RsiHigh;
            DC.RsiLow = ti.RsiLow;
            DC.RsiBuy = ti.RsiRegressionOffsetBuy;
            DC.RsiSell = ti.RsiRegressionOffsetSell;
          }));
          #endregion
          RsiBuy = ti.RsiRegressionOffsetBuy;
          RsiSell = ti.RsiRegressionOffsetSell;
          RsiAverage = ti.RsiRegressionAverage.ToInt();

          this.CanTrade = ti.CanTrade;
          this.CloseBuy = ti.CloseBuy;
          this.CloseSell = ti.CloseSell;
          if (ti.DencityRatio >= 3) {
            this.GoBuy = ti.GoBuy;
            this.GoSell = ti.GoSell;
          }
          corridorOK = ti.CorridorOK;
          this.DencityRatio = ti.DencityRatio;
          this.LotsToTradeBuy = ti.LotsToTradeBuy.ToInt();
          this.LotsToTradeSell = ti.LotsToTradeSell.ToInt();

          #region TakeProfit (Set Trade Limit)
          if (ti.DoTakeProfitBuy) {
            var trade = fw.GetTradeLast(true);
            if (trade != null)
              this.TakeProfitBuy = -(trade.Open + profitMin * fw.PointSize);
            //else
            //  this.TakeProfitBuy = -(price.Ask + ti.TradeStats.corridorSpread);
          }
          if (ti.DoTakeProfitSell) {
            var trade = fw.GetTradeLast(false);
            if (trade != null)
              this.TakeProfitSell = -(trade.Open - profitMin * fw.PointSize);
            //else
            //  this.TakeProfitSell = -(price.Bid - ti.TradeStats.corridorSpread);
          }
          #endregion

          if (ti.CloseLastBuy && !ti.TrancateBuy) fw.FixOrderClose(true, sellOnProfitLast);
          if (ti.TrancateBuy) fw.ClosePositions(0, 1);
          if (ti.CloseLastSell && !ti.TrancateSell) fw.FixOrderClose(false, sellOnProfitLast);
          if (ti.TrancateSell) fw.ClosePositions(1, 0);

          if (false && GoBuy) {
            GoSell = true;
            this.TakeProfitSell = this.TradeInfo.TradeWaveHeight / 2;
          }
          if (false && GoSell) {
            GoBuy = true;
            this.TakeProfitBuy = this.TradeInfo.TradeWaveHeight / 2;
          }

          if (doBiDirection && doSecondTrade) {
            doSecondTrade = false;
            if (fw.GetTrades().OrderBy(t => t.Time).Last().Buy) {
              this.GoSell = true;
              if (!fw.CanTrade(false, this.DencityRatio)) {
                this.DencityRatio = this.DencityRatio * .9;
                this.GoBuy = false;
              }
            } else {
              this.GoBuy = true;
              if (!fw.CanTrade(true, this.DencityRatio)) {
                this.DencityRatio = this.DencityRatio * .9;
                this.GoSell = false;
              }
            }
          }
        };

        #endregion

        #region Run Decider
        var pipsToMC = account.PipsToMC;
          switch (fooNumber) {
            case 11: decideByVoltage_11(); break;
            default: throw new Exception("Unknown Foo Number:" + fooNumber);
          }
          if (Math.Abs(pipsToMC) < spreadAverage15MinInPips) {
            if (pipsToMC < 0) GoBuy = false;
            if (pipsToMC > 0) GoSell = false; 
          } 
        if ((Math.Abs(account.PipsToMC)< spreadAverage || account.IsMarginCall) && summary.BuyPositions != summary.SellPositions) {
          var buy = summary.BuyPositions > summary.SellPositions;
          fw.FixOrderClose(buy,false);
          Thread.Sleep(5000);
        }
        #endregion

        #region Hedging
        if (!account.Hedging && summary.BuyPositions > 0) GoSell = false;
        if (!account.Hedging && summary.SellPositions > 0) GoBuy = false;
        #endregion

        #endregion

        #region Corridor
        if (!corridorOK) GoBuy = GoSell = false;
        #region Show Color
        Dispatcher.BeginInvoke(new Action(() => {
          var color = corridorOK ? Colors.Transparent : Colors.Crimson;
          DC.CanBuyByCorridor = color + "";
          DC.CanSellByCorridor = color + "";
        }));
        #endregion
        #endregion

        #region Force Trade Function
        if (forceOpenTradeBuy) this.CanTrade = this.GoBuy = true;
        if (forceOpenTradeSell) this.CanTrade = this.GoSell = true;
        if (forceOpenTradeBuy || forceOpenTradeSell) {
          this.DencityRatio = 5;
          this.TakeProfitSell = this.TakeProfitBuy = this.TradeInfo.TradeWaveHeight / 2;
        }
        #endregion

        if (PriceGridChanged != null) PriceGridChanged();

        var timeSpanLast = DateTime.Now.Subtract(timeNow).TotalMilliseconds;
        #region UI
        //double powerCurrent = barsBest.Power, powerAverage = bsPeriods.Average(r => r.Power);
        Lib.SetLabelText(lblVolatility, string.Format("{0:n1}/{1:n1}/{2:n1}/{3:n1}",
          spreadAverage15MinInPips, spreadAverage10MinInPips, spreadAverage5MinInPips, spreadAverageInPips));
        Lib.SetBackGround(lblOpenSell, new SolidColorBrush(GoSell ? Colors.PaleGreen : CloseSell ? Colors.LightSalmon : goSell ? Colors.Yellow : Colors.Transparent));
        Lib.SetBackGround(lblOpenBuy, new SolidColorBrush(GoBuy ? Colors.PaleGreen : CloseBuy ? Colors.LightSalmon : goBuy ? Colors.Yellow : Colors.Transparent));
        Lib.SetLabelText(lblServerTime, string.Format("/{1:n0}]", serverTime, timeSpanLast));
        if (this.CanTrade || (summary.BuyPositions + summary.SellPositions) > 0)
          Lib.SetBackGround(wpMain, new SolidColorBrush(Colors.Transparent));
        Lib.SetLabelText(lblOpenBuy, string.Format("{0:n1}", positionBuy));
        Lib.SetLabelText(lblOpenSell, string.Format("{0:n1}", positionSell));
        Dispatcher.BeginInvoke(new Action(() => {
          DC.ServerTime = fw.ServerTime;
          DC.TicksPerMinuteAverageShort = ticksPerMinuteAverageShort;
          DC.TicksPerMinuteAverageLong = ticksPerMinuteAverageLong;
        }));
        #endregion

          #region DB
          //if (doDB) {
          //  var db = new Data.ForexDBDataContext(Properties.Settings.Default.ForexConnectionString);
          //  var t = db.t_Prices;
          //  t.InsertOnSubmit(new HedgeHog.Data.t_Price() {
          //    Account = account, Pair = pair, Date = serverTime,
          //    Row = (decimal)barsBest.Row, IsBuySell = GoBuy ? 1 : GoSell ? -1 : 0,
          //    Ask = (decimal)price.Ask, Bid = (decimal)price.Bid,
          //    Speed = (decimal)barsBest.Speed, Spread = (decimal)barsBest.Spread, Power = (decimal)barsBest.Power
          //  });
          //  db.SubmitChanges();
          //}
          #endregion
      } catch (ThreadAbortException) {
      } catch (Exception exc) {
        if (PriceGridError != null) PriceGridError(exc);
      } finally {
      }
    }
    #endregion

    #region Event Handlers
    void dgBuySellBars_AutoGeneratingColumn(object sender, Microsoft.Windows.Controls.DataGridAutoGeneratingColumnEventArgs e) {
      var column = (Microsoft.Windows.Controls.DataGridBoundColumn)(e.Column);
      var descriptor = e.PropertyDescriptor as System.ComponentModel.MemberDescriptor;
      var displayFormat = descriptor.Attributes.OfType<DisplayFormatAttribute>().FirstOrDefault();
      if (displayFormat != null) column.Binding.StringFormat = displayFormat.DataFormatString;
      var displayName = descriptor.Attributes.OfType<DisplayNameAttribute>().FirstOrDefault();
      if (displayName != null) {
        if (displayName.DisplayName == "") column.Visibility = Visibility.Collapsed;
        else column.Header = displayName.DisplayName;

      }
    }
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
      e.Cancel = true;
      Application.Current.Dispatcher.BeginInvoke(
        DispatcherPriority.Background,
        (DispatcherOperationCallback)delegate(object o) {
        Hide();
        return null;
      },
          null);

    }

    private void Checked(object sender, RoutedEventArgs e) {
      var chb = (sender as CheckBox);
      var name = chb.Name;
      this.SetProperty("_"+name, chb.IsChecked);
    }
    private void txtBSPeriod_TextChanged(object sender, TextChangedEventArgs e) {
      var tb = (sender as TextBox);
      var name = tb.Name;
      try {
        this.SetProperty("_" + name, tb.Text);
      } catch (FormatException exc) {
        MessageBox.Show(tb.Text+Environment.NewLine+ exc.Message);
        e.Handled = true;
        RaisePriceGridError(new Exception("TextBox:" + tb.Name, exc));
      }
    }
    private void TextChanged(object sender, TextChangedEventArgs e) {
      var tb = (sender as TextBox);
      var name = tb.Name;
      this.SetProperty("_" + name, tb.Text);
    }
    private void Voltage_MouseDown(object sender, MouseButtonEventArgs e) {
      popVolts.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
      popVolts.PlacementTarget = sender as UIElement;
      popVolts.IsOpen = !popVolts.IsOpen;
    }
    private void ShowSettings(object sender, MouseButtonEventArgs e) {
      popUpSettings.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
      popUpSettings.PlacementTarget = sender as UIElement;
      popUpSettings.IsOpen = !popUpSettings.IsOpen;
    }
    #endregion

    #region Stuff
    public double TakeProfitNet(double TakePropfitPips, Order2GoAddIn.Summary Summary, bool Buy) {
      return Math.Round(
        TakePropfitPips == 0 ? 0 :
        Buy ? Summary.BuyAvgOpen + TakePropfitPips * fw.PointSize : Summary.SellAvgOpen - TakePropfitPips * fw.PointSize, fw.Digits);
    }
    #endregion

    #region INotifyPropertyChanged Members

    public event PropertyChangedEventHandler PropertyChanged;

    #endregion

  }


  #region Converters
  [ValueConversion(typeof(bool?), typeof(Color))]
  public class BoolToColorConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      var colors = (parameter + "").Split('|');//.Select(r => (Colors)Enum.Parse(typeof(Colors), r, true)).ToArray();
      var color = value == null ? colors[0] : (bool)value ? colors[2] : colors[1];
      return color;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      throw new NotImplementedException();
    }
  }
  #endregion

  #region ViewModel Class
  public class ViewModel : DependencyObject {
    public ViewModel() {
      TradesList = new ListCollectionView(Trades);
    }
    public ObservableCollection<Order2GoAddIn.Trade> Trades = new ObservableCollection<Order2GoAddIn.Trade>();
    public ListCollectionView TradesList { get; set; }

    #region Position (Buy/Sell)
    public int PositionBuy {
      get { return (int)GetValue(PositionBuyProperty); }
      set { SetValue(PositionBuyProperty, value); }
    }
    public static readonly DependencyProperty PositionBuyProperty =
        DependencyProperty.Register("PositionBuy", typeof(int), typeof(ViewModel), new UIPropertyMetadata(0));

    public int PositionSell {
      get { return (int)GetValue(PositionSellProperty); }
      set { SetValue(PositionSellProperty, value); }
    }
    public static readonly DependencyProperty PositionSellProperty =
        DependencyProperty.Register("PositionSell", typeof(int), typeof(ViewModel), new UIPropertyMetadata(0));
    #endregion


   public ObservableCollection<VoltForGrid> Voltage {
      get { return (ObservableCollection<VoltForGrid>)GetValue(VoltageProperty); }
      set { SetValue(VoltageProperty, value); }
    }

    // Using a DependencyProperty as the backing store for Voltage.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty VoltageProperty =
        DependencyProperty.Register("Voltage", typeof(ObservableCollection<VoltForGrid>), typeof(ViewModel));



    public string MainWave {
      get { return (string)GetValue(MainWaveProperty); }
      set { SetValue(MainWaveProperty, value); }
    }
    public static readonly DependencyProperty MainWaveProperty =
        DependencyProperty.Register("MainWave", typeof(string), typeof(ViewModel), new UIPropertyMetadata(Colors.Transparent + ""));



    public string CanBuyByCorridor { get { return (string)GetValue(CanBuyByCorridorProperty); } set { SetValue(CanBuyByCorridorProperty, value); } }
    public static readonly DependencyProperty CanBuyByCorridorProperty = DependencyProperty.Register("CanBuyByCorridor", typeof(string), typeof(ViewModel), new UIPropertyMetadata(Colors.Transparent + ""));
    public string CanSellByCorridor { get { return (string)GetValue(CanSellByCorridorProperty); } set { SetValue(CanSellByCorridorProperty, value); } }
    public static readonly DependencyProperty CanSellByCorridorProperty = DependencyProperty.Register("CanSellByCorridor", typeof(string), typeof(ViewModel), new UIPropertyMetadata(Colors.Transparent + ""));




    public double RateCurrSpread {
      get { return (double)GetValue(RateCurrSpreadProperty); }
      set { SetValue(RateCurrSpreadProperty, value); }
    }
    public static readonly DependencyProperty RateCurrSpreadProperty =
        DependencyProperty.Register("RateCurrSpread", typeof(double), typeof(ViewModel));

    public double RatePrevSpread {
      get { return (double)GetValue(RatePrevSpreadProperty); }
      set { SetValue(RatePrevSpreadProperty, value); }
    }
    public static readonly DependencyProperty RatePrevSpreadProperty =
        DependencyProperty.Register("RatePrevSpread", typeof(double), typeof(ViewModel));




    #region Bar (High/Low)

    public double CorridorSpread { get { return (double)GetValue(CorridorSperadProperty); } set { SetValue(CorridorSperadProperty, value); } }    public static readonly DependencyProperty CorridorSperadProperty = DependencyProperty.Register("CorridorSpread", typeof(double), typeof(ViewModel));

    #endregion



    public DateTime ServerTime { get { return (DateTime)GetValue(ServerTimeProperty); } set { SetValue(ServerTimeProperty, value); } }    public static readonly DependencyProperty ServerTimeProperty = DependencyProperty.Register("ServerTime", typeof(DateTime), typeof(ViewModel));

    public string SpreadTrue {      get { return (string)GetValue(SpreadTrueProperty); }      set { SetValue(SpreadTrueProperty, value); }    }    public static readonly DependencyProperty SpreadTrueProperty =        DependencyProperty.Register("SpreadTrue", typeof(string), typeof(ViewModel));
    public string SpeedTrue {      get { return (string)GetValue(SpeedTrueProperty); }      set { SetValue(SpeedTrueProperty, value); }    }    public static readonly DependencyProperty SpeedTrueProperty =        DependencyProperty.Register("SpeedTrue", typeof(string), typeof(ViewModel));
    public int SpeedWeight { get { return (int)GetValue(SpeedWeightProperty); } set { SetValue(SpeedWeightProperty, value); } }    public static readonly DependencyProperty SpeedWeightProperty = DependencyProperty.Register("SpeedWeight", typeof(int), typeof(ViewModel));

    public double NextTradeDensityBuy { get { return (double)GetValue(NextTradeDensityBuyProperty); } set { SetValue(NextTradeDensityBuyProperty, value); } }    public static readonly DependencyProperty NextTradeDensityBuyProperty = DependencyProperty.Register("NextTradeDensityBuy", typeof(double), typeof(ViewModel));
    public double NextTradeDensitySell { get { return (double)GetValue(NextTradeDensitySellProperty); } set { SetValue(NextTradeDensitySellProperty, value); } }    public static readonly DependencyProperty NextTradeDensitySellProperty = DependencyProperty.Register("NextTradeDensitySell", typeof(double), typeof(ViewModel));

    public int TimeFrame { get { return (int)GetValue(TimeFrameProperty); } set { SetValue(TimeFrameProperty, value); } }    public static readonly DependencyProperty TimeFrameProperty = DependencyProperty.Register("TimeFrame", typeof(int), typeof(ViewModel));
    public int TicksPerMinuteAverageShort { get { return (int)GetValue(TicksPerMinuteAverageShortProperty); } set { SetValue(TicksPerMinuteAverageShortProperty, value); } }    public static readonly DependencyProperty TicksPerMinuteAverageShortProperty = DependencyProperty.Register("TicksPerMinuteAverageShort", typeof(int), typeof(ViewModel));
    public int TicksPerMinuteAverageLong { get { return (int)GetValue(TicksPerMinuteAverageLongProperty); } set { SetValue(TicksPerMinuteAverageLongProperty, value); } }    public static readonly DependencyProperty TicksPerMinuteAverageLongProperty = DependencyProperty.Register("TicksPerMinuteAverageLong", typeof(int), typeof(ViewModel));

    public double CorridorMinimum { get { return (double)GetValue(CorridorMinimumProperty); } set { SetValue(CorridorMinimumProperty, value); } }    public static readonly DependencyProperty CorridorMinimumProperty = DependencyProperty.Register("CorridorMinimum", typeof(double), typeof(ViewModel));
    public double CorridorMinimumToTrade { get { return (double)GetValue(CorridorMinimumToTradeProperty); } set { SetValue(CorridorMinimumToTradeProperty, value); } }    public static readonly DependencyProperty CorridorMinimumToTradeProperty = DependencyProperty.Register("CorridorMinimumToTrade", typeof(double), typeof(ViewModel));

    public int RsiLow { get { return (int)GetValue(RsiLowProperty); } set { SetValue(RsiLowProperty, value); } }    public static readonly DependencyProperty RsiLowProperty = DependencyProperty.Register("RsiLow", typeof(int), typeof(ViewModel));
    public int RsiHigh { get { return (int)GetValue(RsiHighProperty); } set { SetValue(RsiHighProperty, value); } }    public static readonly DependencyProperty RsiHighProperty = DependencyProperty.Register("RsiHigh", typeof(int), typeof(ViewModel));



    public int RsiBuy { get { return (int)GetValue(RsiBuyProperty); } set { SetValue(RsiBuyProperty, value); } }    public static readonly DependencyProperty RsiBuyProperty = DependencyProperty.Register("RsiBuy", typeof(int), typeof(ViewModel));
    public int RsiSell { get { return (int)GetValue(RsiSellProperty); } set { SetValue(RsiSellProperty, value); } }    public static readonly DependencyProperty RsiSellProperty = DependencyProperty.Register("RsiSell", typeof(int), typeof(ViewModel));

  }
  #endregion

}
