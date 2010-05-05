using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
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
using System.Threading;
using ControlExtentions;
using Order2GoAddIn;
using FXW = Order2GoAddIn.FXCoreWrapper;
using HedgeHog;
using HedgeHog.Models;
namespace HedgeHog {
  public partial class HedgeHogMainWindow : WindowModel,INotifyPropertyChanged,WpfPersist.IUserSettingsStorage {

    #region IUserSettingsStorage Members
    public WpfPersist.SaveDelegate Save { get; set; }
    #endregion
    #region Properties

    Thread threadCloseBuy = new Thread(() => { });
    Thread threadCloseSell = new Thread(() => { });

    public string _txtPassword, _txtAccNum;
    public int _txtLeverage;
    public double _txtTradeDelta, _txtStartingBalance;

    public string title { get { return AccountNumber + ":" + pair; } }
    public string AccountNumber { get { return _txtAccNum; } }
    string password { get { return _txtPassword; } }

    public ComboBoxItem LotsToTradeBuy { get; set; }
    int lotsToTradeBuy { get { return Convert.ToInt32(LotsToTradeBuy.Content); } }

    public ComboBoxItem LotsToTradeSell { get; set; }
    int lotsToTradeSell { get { return Convert.ToInt32(LotsToTradeSell.Content); } }

    public ComboBoxItem Pair { get; set; }
    string pair { get { return Pair == null ? "" : Pair.Content + ""; } }

    double tradeDelta { get { return _txtTradeDelta; } }

    double _accountEquity = 0;
    public double AccountEquity {
      get { return _accountEquity; }
      set { 
        _accountEquity = value; 
        RaisePropertyChangedCore();
      }
    }

    double _netPL;
    public double NetPL {
      get { return _netPL; }
      set { _netPL = value; RaisePropertyChangedCore(); }
    }

    double _startingBalance;
    public double StartingBalance {
      get { return _startingBalance; }
      set { _startingBalance = value; RaisePropertyChangedCore(); }
    }

    string _ruleToExit = "";

    public string RuleToExit {
      get { return _ruleToExit; }
      set { _ruleToExit = value; RaisePropertyChangedCore(); }
    }

    public double _txtPriceToAdd;
    public double PriceToAdd { get { return _txtPriceToAdd; } }

    enum Condition { None,LessThen, MoreThen };
    double priceToExit { get { return double.Parse(RuleToExit.Split(new[] { '>', '<' }, StringSplitOptions.RemoveEmptyEntries).DefaultIfEmpty("0").First()); } }
    Condition conditionToExit { get { return RuleToExit[0] == '>' ? Condition.MoreThen : RuleToExit[0] == '<' ? Condition.LessThen : Condition.None; } }

    public bool _chkDemo;
    bool isDemo { get { return _chkDemo; } }

    public bool _chkTradeDistanceUnisex;
    bool tradeDistanceUnisex { get { return _chkTradeDistanceUnisex; } }

    public bool _chkAutoPilot;
    bool isAutoPilot { get { return _chkAutoPilot; } }

    public bool _chkAutoAdjust;
    bool isAutoAdjust { get { return _chkAutoAdjust; } }

    int leverage { get { return _txtLeverage; } }
    private Thread threadProc;
    private Thread threadWait;

    string logFileName = "Log.txt";
    object Log {
      set {
        var exc = value as Exception;
        var message = exc == null ? value+"": exc.Message;
        txtAccNum.Dispatcher.BeginInvoke(new Action(delegate() {

          var lines = txtLog.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).ToList();
          var time = DateTime.Now.ToString("[dd HH:mm:ss] ");
          lines.Add(time + message + Environment.NewLine);
          txtLog.Text = string.Join(Environment.NewLine, lines.Skip(lines.Count - 30).ToArray());

          txtLog.ScrollToEnd();
          var text = message + Environment.NewLine + (exc == null ? "" : exc.StackTrace + Environment.NewLine);
          while (exc != null && (exc = exc.InnerException) != null)
            text += "**************** Inner ***************" + Environment.NewLine + exc.Message + Environment.NewLine + exc.StackTrace + Environment.NewLine;
          System.IO.File.AppendAllText(logFileName, text);
        })
        );
      }
    }

    string _usableMargin;
    public string UsableMargin {
      get { return _usableMargin; }
      set { _usableMargin = value; RaisePropertyChangedCore(); }
    }
    double _accountBalance;
    public double AccountBalance {
      get { return _accountBalance; }
      set { _accountBalance = value; RaisePropertyChangedCore(); }
    }
    int _pipsToMC;
    public int PipsToMC {
      get { return _pipsToMC; }
      set { _pipsToMC = value; RaisePropertyChangedCore(); }
    }

    public int _txtPipsToMCHistory;
    int pipsToMCHistory {
      get { return _txtPipsToMCHistory; }
    }

    public int _txtMinEquityHistory;
    int minEquityHistory {
      get { return _txtMinEquityHistory; }
      set {
        if (value > 0){
          SetBindedText(txtMinEquityHistory, value);
        }
      }
    }

    private static void SetBindedText(TextBox element, object value) {
      var b = element.GetBindingExpression(TextBox.TextProperty);
      var path = System.Text.RegularExpressions.Regex.Match(b.ParentBinding.Path.Path, @"\[(.+)\]").Groups[1] + "";
      var dataItem = (b.DataItem as WpfPersist.UserSettingsExtension.InternalBinder).Dictionary;
      dataItem[path] = value + "";
      b.UpdateTarget();
    }
    int _lotsLeft;
    public int LotsLeft {
      get { return _lotsLeft; }
      set { _lotsLeft = value; RaisePropertyChangedCore(); }
    }


    double _sellPL;
    public double SellPL {
      get { return _sellPL; }
      set {        _sellPL = value;        RaisePropertyChangedCore();      }
    }
    double _sellLPP;
    public double SellLPP {
      get { return _sellLPP; }
      set {        _sellLPP = value; RaisePropertyChangedCore();     }
    }
    double _sellPos;
    public double SellPositions {
      get { return _sellPos; }
      set { _sellPos = value; RaisePropertyChangedCore(); }
    }
    int _sellPips;
    public int SellPips {
      get { return _sellPips; }
      set { _sellPips = value; RaisePropertyChangedCore(); }
    }
    double _sellLots;
    public double SellLots {
      get { return _sellLots; }
      set {        _sellLots = value; RaisePropertyChangedCore();      }
    }
    double _buyPL;
    public double BuyPL {
      get { return _buyPL; }
      set {        _buyPL = value; RaisePropertyChangedCore();      }
    }
    double _buyLPP;
    public double BuyLPP {
      get { return _buyLPP; }
      set { _buyLPP = value; RaisePropertyChangedCore(); }
    }
    double _buyPos;
    public double BuyPositions {
      get { return _buyPos; }
      set {        _buyPos = value; RaisePropertyChangedCore();      }
    }
    int _buyPips;
    public int BuyPips {
      get { return _buyPips; }
      set {        _buyPips = value; RaisePropertyChangedCore();      }
    }
    double _buyLots;
    public double BuyLots {
      get { return _buyLots; }
      set { _buyLots = value; RaisePropertyChangedCore(); }
    }

    double _buyPipsToNet;
    public double BuyPipsToNet {
      get { return _buyPipsToNet; }
      set { _buyPipsToNet = value; RaisePropertyChangedCore(); }
    }
    double _sellPipsToNet;
    public double SellPipsToNet {
      get { return _sellPipsToNet; }
      set { _sellPipsToNet = value; RaisePropertyChangedCore(); }
    }

    double _spread;
    public double Spread {
      get { return _spread; }
      set { _spread = value; RaisePropertyChangedCore(); }
    }
    double? _spreadCma;
    public double? SpreadCma {
      get { return _spreadCma; }
      set { _spreadCma = value; RaisePropertyChangedCore(); }
    }
    #endregion

    Order2GoAddIn.FXCoreWrapper fw;
    public HedgeHogMainWindow():this("") { }
    public HedgeHogMainWindow(string name){
      if( name+"" != "") this.Name = name;
      System.Diagnostics.Debug.WriteLine(AppDomain.CurrentDomain.GetData("DataDirectory"));
      InitializeComponent();
      if (isMainWindow)
        app.FXCM.LoginError += new Order2GoAddIn.CoreFX.LoginErrorHandler(FXCM_LoginError);
      app.ClosingBalanceChanged += new EventHandler<ClosingBalanceChangedEventArgs>(app_ClosingBalanceChanged);
    }

    bool isMainWindow { get { return this.Name == ""; } }
    void FXCM_LoginError(Exception exc) {
      Log = exc;
    }
    private void Login(object sender, RoutedEventArgs e) {
      chartingWindow.Show();
      corridorsWindow.Show();
      Dispatcher.BeginInvoke(new Action(() => {
        try {
          if (fw.LogOn(pair,app.FXCM, AccountNumber, password, Properties.Settings.Default.ServerUrl, isDemo))
            chartingWindow.Dispatcher.BeginInvoke(new Action(() => {
              chartingWindow.ProcessPrice(null);
            }));
        } catch (Exception exc) {
          System.Windows.MessageBox.Show(exc.Message);
        }
      }));
    }

    #region Event Handlers

    private void OnPairChanged(object sender, SelectionChangedEventArgs e) {
      if (fw != null && fw.IsLoggedIn) {
        try {
          fw.Pair = pair;
          dataGrid1.ItemsSource = fw.GetTrades().ToList();
        } catch (Order2GoAddIn.FXCoreWrapper.PairNotFoundException exc) {
          MessageBox.Show(exc.Message);
          ((System.Windows.Controls.ListBoxItem)e.RemovedItems[0]).IsSelected = true;
          return;
        }
      }
      Log = "Pair was changed to " + pair;
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
      if (Name == "")
        Dispatcher.Invoke(new Action(() => {
          if (fw != null)
            fw.Dispose();
          if (chartingWindow != null)
            chartingWindow.Close();
        }));
      else
        Window_Closing_Hide(sender, e);
    }


    private void Window_Initialized(object sender, EventArgs e) {
      Dispatcher.Invoke(new Action(() => {
        cmbPair.ResetText();
        cmbLotsToTradeBuy.ResetText();
        cmbLotsToTradeSell.ResetText();
        if (pair == "") {
          MessageBox.Show("Pair is not selected!");
          return;
        }
        if (isMainWindow) System.IO.File.Delete(logFileName);
        fw = new Order2GoAddIn.FXCoreWrapper();
        chartingWindow = new HedgeHog.Charting(this.Name, fw);
        corridorsWindow = new Corridors(this.Name);
        chartingWindow.TicksChanged += chartingWindow_TicksChanged;
        chartingWindow.PriceGridChanged += ProcessPrice;
        chartingWindow.PriceGridError += chartingWindow_PriceGridError;
        dataGrid1.AutoGeneratingColumn += new EventHandler<Microsoft.Windows.Controls.DataGridAutoGeneratingColumnEventArgs>(dataGrid1_AutoGeneratingColumn);
        RaisePropertyChanged(()=> title);
      }));

      //Window w = sender as Window;
      //w.Height = Properties.Settings.Default.MainWindowSize.Height;
      //w.Width = Properties.Settings.Default.MainWindowSize.Width;
      //w.Top = Properties.Settings.Default.MainWindowPos.Y;
      //w.Left = Properties.Settings.Default.MainWindowPos.X;

    }

    void chartingWindow_TicksChanged(object sender, Charting.TickChangedEventArgs e) {
      corridorsWindow.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => {
        corridorsWindow.AddTicks(fw.GetPrice(), e.Ticks, e.VoltsByTick, e.VoltageHigh, e.VoltageCurr, e.PriceMaxAverage, 0, 0, 0, e.TimeHigh, e.TimeCurr, new double[] { });
      }));
    }


    void chartingWindow_PriceGridError(Exception exc) {
      Log = exc;
    }

    #endregion

    void dataGrid1_AutoGeneratingColumn(object sender, Microsoft.Windows.Controls.DataGridAutoGeneratingColumnEventArgs e) {
      if (e.PropertyName == "GrossPL") ((Microsoft.Windows.Controls.DataGridBoundColumn)(e.Column)).Binding.StringFormat = "{0:c0}";
    }

    void ShowSpread(Order2GoAddIn.Price Price) {
      var digits = fw.Digits;
      Spread = fw.InPips(Price.Ask - Price.Bid);
      SpreadCma = SpreadCma.Cma(50, Spread);
    }

    void app_ClosingBalanceChanged(object sender, ClosingBalanceChangedEventArgs e) {
      if (sender != this && StartingBalance > 0 && StartingBalance < e.ClosingBalance) StartingBalance = e.ClosingBalance;
    }

    void ShowAccount(Order2GoAddIn.Account Account, Order2GoAddIn.Summary Summary) {
      AccountBalance = Account.Balance;
      PipsToMC = Account.PipsToMC;
      minEquityHistory = (int)Account.Equity;
      LotsLeft = (int)(Account.UsableMargin * leverage);
      var summaries = FXW.GetSummaries();
      var tradesAll = FXW.GetTrades("");
      NetPL = tradesAll.GrossInPips();
      UsableMargin = string.Format("{0:c0}/{1:p1}", Account.UsableMargin, Account.UsableMargin / Account.Equity);
      AccountEquity = Account.Equity;// string.Format("{0:c0}/{1:n1}", Account.Equity, netPL);
      var doCloseLotsOfTrades = tradesAll.Length > app.MainWindows.Count + 1 && Account.Gross > 0;
      Commission = FXW.CommisionPending;
      var haveGoodProfit = NetPL >= DensityAverage;
      if (StartingBalance > 0 && Account.Equity >= StartingBalance ||
        haveGoodProfit ||
        doCloseLotsOfTrades ||
        (priceToExit > 0 &&
        ((conditionToExit == Condition.LessThen && Summary.PriceCurrent.Average < priceToExit) ||
          (conditionToExit == Condition.MoreThen && Summary.PriceCurrent.Average > priceToExit)
        ))
        ) {
        ClosePositions(this, new RoutedEventArgs());
        StartingBalance = Math.Round(Order2GoAddIn.FXCoreWrapper.GetAccount().Equity * (1 + PriceToAdd / 100), 0);
        app.RaiseClosingalanceChanged(this, StartingBalance.ToInt());
        RuleToExit = "0";
      }

    }
    void ShowSummary(Order2GoAddIn.Summary Summary, Order2GoAddIn.Account Account) {
      var summary = Summary ?? new Order2GoAddIn.Summary();
      double buyLossPerLotK;
      double sellLossPerLotK;
      SellPL = summary.SellNetPL;
      SellLots = summary.SellLots;
      SellLPP = summary.SellLPP;
      SellPositions = summary.SellPositions;
      SellPips = (int)(summary.SellDelta / summary.PointSize);
      BuyPL = summary.BuyNetPL;
      BuyLots = summary.BuyLots;
      BuyLPP = summary.BuyLPP;
      BuyPositions = summary.BuyPositions;
      BuyPips = (int)(summary.BuyDelta / summary.PointSize);
      var totalPips = (summary.BuyPriceFirst - summary.SellPriceFirst) / fw.PointSize;
      BuyPipsToNet =summary.BuyNetPLPip;
      SellPipsToNet= summary.SellNetPLPip;
      buyLossPerLotK = summary.BuyLots > 0 ? summary.BuyNetPL / (summary.BuyLots / 1000) : 0;
      sellLossPerLotK = summary.SellLots > 0 ? summary.SellNetPL / (summary.SellLots / 1000) : 0;
    }
    public double DensityAverage { get { return Charting.DensityAverage; } }
    double _commission;
    public double Commission {
      get { return _commission; }
      set { _commission = value; RaisePropertyChangedCore(); }
    }
    void fw_PriceChanged(Order2GoAddIn.Price Price) {
      if (threadProc != null && threadProc.ThreadState == ThreadState.Running) {
        if (threadWait != null && threadWait.ThreadState == ThreadState.Running) threadWait.Abort();
        threadWait = new Thread(delegate() {
          threadProc.Join();
          threadProc = new Thread(delegate() { ProcessPrice(); });
          threadProc.Priority = ThreadPriority.Lowest;
          try {
            threadProc.Start();
          } catch (ThreadStateException) { }
        });
        threadWait.Start();
      } else {
        threadProc = new Thread(delegate() { ProcessPrice(); });
        threadProc.Priority = ThreadPriority.Lowest;
        threadProc.Start();
      }
    }
    private void ProcessPrice() {
      try {
        RaisePropertyChanged(() => DensityAverage);
        if (Visibility == Visibility.Hidden) return;
        Order2GoAddIn.Price Price = fw.GetPrice();
        var digits = fw.Digits;
        ShowSpread(Price);

        var account = FXW.GetAccount();
        var summary = fw.GetSummary() ?? new Order2GoAddIn.Summary();
        ShowAccount(account, summary);
        ShowSummary(summary, account);

        #region Trade (Open)
        var canBuy = chartingWindow.GoBuy && !chartingWindow.CloseBuy &&
          (chartingWindow.CanTrade || summary.BuyLots > 0) &&
          (Lib.GetChecked(chkCanBuy).Value || summary.BuyLots > 0);
        var canSell = chartingWindow.GoSell && !chartingWindow.CloseSell &&
          (chartingWindow.CanTrade || summary.SellLots > 0) &&
          (Lib.GetChecked(chkCanSell).Value || summary.SellLots > 0);
        if ((isAutoPilot || isAutoAdjust || summary.SellPositions + summary.BuyPositions > 0)) {
          var buyTradeDelta = tradeDelta * chartingWindow.DencityRatio;// *averageProfitCMA30_Sell / (averageProfitCMA30_Sell + averageProfitCMA30_Buy);
          var takeProfitBuy = summary == null || summary.BuyPositions == 0 || chartingWindow.TakeProfitBuy < 0 ? chartingWindow.TakeProfitBuy
            : -chartingWindow.TakeProfitNet(chartingWindow.TakeProfitBuy, summary, true);
          if ((isAutoPilot || summary.BuyPositions > 0) && canBuy /*&& lots > 0 && fw.CanTrade(true, buyTradeDelta)*/)
            try {
              var lots = chartingWindow.LotsToTradeBuy > 1000 ? chartingWindow.LotsToTradeBuy : chartingWindow.LotsToTradeBuy * lotsToTradeBuy * 1000;
              var l = fw.CanTrade2(true, buyTradeDelta, lots,tradeDistanceUnisex);
              lots = chartingWindow.LotsToTradeBuy > 1 && l < lots ? 0 : l;
              if (lots > 0)
                fw.FixOrderOpen(true, lots, takeProfitBuy, chartingWindow.StopLossBuy, chartingWindow.TradeInfo.ToString());
            } catch (Order2GoAddIn.FXCoreWrapper.OrderExecutionException exc) {
              Log = exc;
            }
          var sellTradeDelta = tradeDelta * chartingWindow.DencityRatio;// *averageProfitCMA30_Buy / (averageProfitCMA30_Sell + averageProfitCMA30_Buy);
          var takeProfitSell = summary == null || summary.SellPositions == 0 || chartingWindow.TakeProfitSell < 0 ? chartingWindow.TakeProfitSell
            : -chartingWindow.TakeProfitNet(chartingWindow.TakeProfitSell, summary, false);
          if ((isAutoPilot || summary.SellPositions > 0) && canSell/* && lots > 0 && fw.CanTrade(false, sellTradeDelta)*/)
            try {
              var lots = chartingWindow.LotsToTradeSell > 1000 ? chartingWindow.LotsToTradeSell : chartingWindow.LotsToTradeSell * lotsToTradeSell * 1000;
              var l = fw.CanTrade2(false, sellTradeDelta, lots,tradeDistanceUnisex);
              lots = chartingWindow.LotsToTradeSell > 1 && l < lots ? 0 : l;
              if (lots > 0)
                fw.FixOrderOpen(false, lots, takeProfitSell, chartingWindow.StopLossSell, chartingWindow.TradeInfo.ToString());
            } catch (Order2GoAddIn.FXCoreWrapper.OrderExecutionException exc) {
              Log = exc;
            }

          if (isAutoAdjust) {
            var stats = new DispatcherOperationStatus[] { DispatcherOperationStatus.Executing, DispatcherOperationStatus.Pending };
            if (summary != null && summary.BuyPositions > 0 && takeProfitBuy > 0) {
              fw.FixOrderSetNetLimits(Math.Abs(takeProfitBuy), true);
              //if( setLimitThreadBuy != null ) 
              //  setLimitThreadBuy.Abort();
              //setLimitThreadBuy = new Thread(delegate() { fw.FixOrder_SetNetLimits(Math.Abs(takeProfitBuy), true); }) { Priority = ThreadPriority.Lowest };
              //setLimitThreadBuy.Start();
            }
            if (summary != null && summary.SellPositions > 0 && takeProfitSell > 0) {
              fw.FixOrderSetNetLimits(Math.Abs(takeProfitSell), false);
              //if (setLimitThreadSell != null)
              //  setLimitThreadSell.Abort();
              //setLimitThreadSell = new Thread(delegate() { fw.FixOrder_SetNetLimits(Math.Abs(takeProfitSell), false); }) { Priority = ThreadPriority.Lowest };
              //setLimitThreadSell.Start();
            }
          }
        }
        #endregion
        ThreadState[] ts = new[] { ThreadState.Running, ThreadState.WaitSleepJoin };
        if (isAutoAdjust) {
          if (chartingWindow.CloseBuy && !ts.Contains(threadCloseBuy.ThreadState))
            fw.CloseProfit(true, -10000);

          if (chartingWindow.CloseSell && !ts.Contains(threadCloseBuy.ThreadState))
            fw.CloseProfit(false, -10000);
        }

      } catch (ThreadAbortException) {
      } catch (Exception exc) {
        Log = exc;
      }
    }

    private void CloseProfit(bool Buy, double MinProfit) {
      Thread t = new Thread(() => {
        try {
          var tradesIDs = fw.GetTradesToClose(Buy, MinProfit).Select(r => r.Id).ToArray();
          while (tradesIDs.Length > 0) {
            foreach (var tradeID in tradesIDs)
              try {
                Order2GoAddIn.FXCoreWrapper.FixOrderClose(tradeID);
              } catch { }
            tradesIDs = fw.GetTradesToClose(Buy, MinProfit).Select(r => r.Id).ToArray();
          }
        } catch (Exception exc) {
          Log = exc;
        }
      });
      if (Buy) threadCloseBuy = t; else threadCloseSell = t;
      t.Start();
    }
    public static double CMA(double MA, double Periods, double NewValue) {
      return MA + (NewValue - MA) / (Periods + 1);
    }

    #region UI Event Handlers

    #endregion
    private Charting chartingWindow;
    private Corridors corridorsWindow;
    private void btnOpenChart_Click(object sender, RoutedEventArgs e) {
      Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => {
        chartingWindow.Show();
        chartingWindow.WindowState = WindowState.Normal;
        chartingWindow.Activate();
        chartingWindow.fw_PriceChanged();
        corridorsWindow.Show();
        corridorsWindow.Activate();
      }));
    }

    private void btnOpenDB_Click(object sender, RoutedEventArgs e) {
      if (Save == null)
        MessageBox.Show("Use main mindow to save settings.");
      else
        Save();
      //      var of = new Microsoft.Win32.OpenFileDialog() { Filter = "SQL Server database (.mdb)|*.mdb", DefaultExt = "*.mdb" };
      //      if (!of.ShowDialog(this).Value) return;
      //      Properties.Settings.Default.ForexConnectionString =
      //@"Data Source=.\SQLEXPRESS2008;AttachDbFilename=" + of.FileName + ";Integrated Security=True;Connect Timeout=30;User Instance=True";
    }
    private void btnOpenMain_Click(object sender, RoutedEventArgs e) {
      var mw = app.MainWindows.FirstOrDefault(w => !w.IsVisible);
      if (mw == null) {
        mw = new HedgeHogMainWindow("MainWindow" + app.MainWindows.Count);
        app.MainWindows.Add(mw);
      }
      mw.Show();
    }
    App app { get { return Application.Current as App; } }

    private void TextChanged(object sender, TextChangedEventArgs e) {
      var tb = (sender as TextBox);
      var name = tb.Name;
      this.SetProperty("_" + name, tb.Text);
    }

    static bool isClosingPositions = false;
    private void ClosePositions(object sender, RoutedEventArgs e) {
      if (isClosingPositions) return;
      try {
        isClosingPositions = true;
        Order2GoAddIn.FXCoreWrapper.ClosePositions();
      } catch (Exception exc) {
        Dispatcher.BeginInvoke(new Action(() => MessageBox.Show(exc.Message)));
        return;
      } finally {
        isClosingPositions = false;
      }
    }
    private void TrancatePositions(object sender, RoutedEventArgs e) {
      var summury = fw.GetSummary();
      if (summury.SellPositions > 1 && summury.BuyPositions > 1) {
        try {
          fw.FixOrderClose(true, false);
          fw.FixOrderClose(false, false);
          summury = fw.GetSummary();
        } catch (Exception exc) {
          Dispatcher.BeginInvoke(new Action(() => MessageBox.Show(exc.Message)));
          return;
        }
      }
    }
  }
  public class ToolTips {
    public string CurrentDirectory { get { return AppDomain.CurrentDomain.BaseDirectory; } }
  }
}
