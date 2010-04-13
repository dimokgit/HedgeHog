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
using FXW = Order2GoAddIn.FXCoreWrapper;
using HedgeHog;
namespace HedgeHog {
  public partial class HedgeHogMainWindow : Window,INotifyPropertyChanged,WpfPersist.IUserSettingsStorage {

    #region IUserSettingsStorage Members
    public WpfPersist.SaveDelegate Save { get; set; }
    #endregion
    #region Properties

    Thread threadCloseBuy = new Thread(() => { });
    Thread threadCloseSell = new Thread(() => { });

    public string _txtPassword, _txtAccNum;
    public int? _txtLeverage;
    public double? _txtTradeDelta, _txtStartingBalance, _txtPipsToMCHistory, _txtMinEquityHistory;

    public string title {
      get {
        return AccountNumber + ":" + pair;
      }
    }
    public string AccountNumber {
      get { return _txtAccNum ?? Lib.GetTextBoxText(txtAccNum); }
      set { Lib.SetTextBoxText(txtAccNum, value); }
    }
    string password { get { return _txtPassword ?? Lib.GetTextBoxText(txtPassword); } }
    byte lotsToTradeBuy { get { return byte.Parse(Lib.GetSelected(cmbLotsToTradeBuy)); } }
    byte lotsToTradeSell { get { return byte.Parse(Lib.GetSelected(cmbLotsToTradeSell)); } }
    string pair {
      get {
        try {
          return ((ContentControl)cmbPair.SelectedItem).Content + "";
        } catch (Exception exc) {
          MessageBox.Show(exc.Message);
          return "";
        }
      }
      set { cmbPair.Text = value; }
    }
    double tradeDelta { get { return _txtTradeDelta.HasValue ? _txtTradeDelta.Value : double.Parse(Lib.GetTextBoxText(txtTradeDelta)); } }
    double startingBalance {
      get { return _txtStartingBalance.HasValue ? _txtStartingBalance.Value : double.Parse(Lib.GetTextBoxText(txtStartingBalance)); }
      set { Lib.SetTextBoxText(txtStartingBalance, Math.Round(value, 0) + ""); }
    }

    public string _txtPriceToExit;
    public string ruleToExit {
      get { return _txtPriceToExit ?? Lib.GetTextBoxText(txtPriceToExit); }
      set { Lib.SetTextBoxText(txtPriceToExit, value + ""); }
    }

    public double? _txtPriceToAdd;
    public double PriceToAdd { get { return _txtPriceToAdd.HasValue ? _txtPriceToAdd.Value : Lib.GetTextBoxTextDouble(txtPriceToAdd); } }

    enum Condition { None,LessThen, MoreThen };
    double priceToExit { get { return double.Parse(ruleToExit.Split(new[] { '>', '<' }, StringSplitOptions.RemoveEmptyEntries)[0]); } }
    Condition conditionToExit { get { return ruleToExit[0] == '>' ? Condition.MoreThen : ruleToExit[0] == '<' ? Condition.LessThen : Condition.None; } }

    bool isDemo { get { return Lib.GetChecked(chkDemo).Value; } }
    bool tradeDistanceUnisex { get { return Lib.GetChecked(chkTradeDistanceUnisex).Value; } set { Lib.SetChecked(chkTradeDistanceUnisex, value); } }
    bool isAutoPilot { get { return Lib.GetChecked(chkAutoPilot).Value; } set { Lib.SetChecked(chkAutoPilot, value); } }
    bool isAutoAdjust { get { return Lib.GetChecked(chkAutoAdjust).Value; } set { Lib.SetChecked(chkAutoAdjust, value); } }
    int leverage { get { return _txtLeverage.HasValue?_txtLeverage.Value : int.Parse(Lib.GetTextBoxText(txtLeverage)); } }
    private Thread threadProc;
    private Thread threadWait;

    double spreadCMA;

    string logFileName = "Log.txt";
    object Log {
      set {
        var exc = value as Exception;
        var message = exc == null ? value+"": exc.Message;
        txtAccNum.Dispatcher.BeginInvoke(new Action(delegate() {
          txtLog.Text += DateTime.Now.ToString("HH:mm:ss") + " : " + message + Environment.NewLine;
          txtLog.ScrollToEnd();
          var text = message + Environment.NewLine + (exc == null ? "" : exc.StackTrace + Environment.NewLine);
          while (exc != null && (exc = exc.InnerException) != null)
            text += "**************** Inner ***************" + Environment.NewLine + exc.Message + Environment.NewLine + exc.StackTrace + Environment.NewLine;
          System.IO.File.AppendAllText(logFileName, text);
        })
        );
      }
    }

    double _usableMargin;
    double usableMargin {
      get { return _usableMargin; }
      set {
        _usableMargin = value;
        Lib.SetLabelText(lblUsableMargin, string.Format("{0:c0}", value));
      }
    }
    double _accountBalance;
    double accountBalance {
      get { return _accountBalance; }
      set {
        _accountBalance = value;
        Lib.SetLabelText(lblAccountBalance, string.Format("{0:c0}", value));
      }
    }
    int _pipsToMC;
    int pipsToMC {
      get { return _pipsToMC; }
      set {
        _pipsToMC = value;
        pipsToMCHistory = Math.Abs(value);
        Lib.SetLabelText(lblPipsToMC, value + "");
      }
    }
    int pipsToMCHistory {
      get { return Lib.GetTextBoxTextInt(txtPipsToMCHistory); }
      set {
        if (value > 0)
          Lib.SetTextBoxText(txtPipsToMCHistory, Math.Min(pipsToMCHistory, value) + "");
      }
    }
    int minEquityHistory {
      get { return Lib.GetTextBoxTextInt(txtMinEquityHistory); }
      set {
        if (value > 0)
          Lib.SetTextBoxText(txtMinEquityHistory, Math.Min(minEquityHistory, value) + "");
      }
    }
    int _lotsLeft;
    int lotsLeft {
      get { return _lotsLeft; }
      set {
        _lotsLeft = value;
        Lib.SetLabelText(lblLotsLeft,  value.ToString("n0"));
      }
    }


    double _sellPL;
    double sellPL {
      get { return _sellPL; }
      set {
        _sellPL = value;
        Lib.SetLabelText(lblSellPL, string.Format("{0:c0}", value));
      }
    }
    double _sellLPP;
    double sellLPP {
      get { return _sellLPP; }
      set {
        _sellLPP = value;
        Lib.SetLabelText(lblSellLPP, string.Format("{0:n0}", value));
      }
    }
    double _sellPos;
    double sellPos {
      get { return _sellPos; }
      set {
        _sellPos = value;
        Lib.SetLabelText(lblSellPositions, value + "");
      }
    }
    int _sellPips;
    int sellPips {
      get { return _sellPips; }
      set {
        _sellPips = value;
        Lib.SetLabelText(lblSellPips, value + "");
      }
    }
    double _sellLots;
    double sellLots {
      get { return _sellLots; }
      set {
        _sellLots = value;
        Lib.SetLabelText(lblSellLots, string.Format("{0:n0}", value));
      }
    }
    double _buyPL;
    double buyPL {
      get { return _buyPL; }
      set {
        _buyPL = value;
        Lib.SetLabelText(lblBuyPL, string.Format("{0:c0}", value));
      }
    }
    double _buyLPP;
    double buyLPP {
      get { return _buyLPP; }
      set {
        _buyLPP = value;
        Lib.SetLabelText(lblBuyLPP, string.Format("{0:n0}", value));
      }
    }
    double _buyPos;
    double buyPos {
      get { return _buyPos; }
      set {
        _buyPos = value;
        Lib.SetLabelText(lblBuyPositions, value + "");
      }
    }
    int _buyPips;
    int buyPips {
      get { return _buyPips; }
      set {
        _buyPips = value;
        Lib.SetLabelText(lblBuyPips, value + "");
      }
    }
    double _buyLots;
    double buyLots {
      get { return _buyLots; }
      set {
        _buyLots = value;
        Lib.SetLabelText(lblBuyLots, string.Format("{0:n0}", value));
      }
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
        if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("title"));
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

    double ShowSpread(Order2GoAddIn.Price Price) {
      var digits = fw.Digits;
      var spread = Price.Ask - Price.Bid;
      spreadCMA = CMA(spreadCMA == 0 ? spread : spreadCMA, 50, spread);
      Lib.SetLabelText(lblSpread, string.Format("{0:n1}/{1:n2}", spread / fw.PointSize, spreadCMA / fw.PointSize));
      return spread;
    }

    public double ProfitPercent;
    void ShowAccount(Order2GoAddIn.Account Account, Order2GoAddIn.Summary Summary) {
      accountBalance = Account.Balance;
      usableMargin = Account.UsableMargin;
      pipsToMC = Account.PipsToMC;
      minEquityHistory = (int)Account.Equity;
      lotsLeft = (int)(Account.UsableMargin * leverage);
      Lib.SetLabelText(lblUsableMargin, string.Format("{0:c0}/{1:p1}", Account.UsableMargin, Account.UsableMargin / Account.Equity));
      Lib.SetLabelText(lblAccountEquity, string.Format("{0:c0}", Account.Equity));
      var doCloseLotsOfTrades = FXW.GetTrades("").Length > app.MainWindows.Count + 1 && Account.Equity > Account.Balance;
      Commission = FXW.GetTrades("").Sum(t => t.Lots) / 10000;
      var haveGoodProfit = (Account.Equity - Account.Balance) > (DensityAverage + Commission);
      if (startingBalance > 0 && Account.Equity >= startingBalance ||
        haveGoodProfit ||
        (priceToExit > 0 &&
        ((conditionToExit == Condition.LessThen && Summary.PriceCurrent.Average < priceToExit) ||
          (conditionToExit == Condition.MoreThen && Summary.PriceCurrent.Average > priceToExit)
        ))
        ) {
         ClosePositions(this, new RoutedEventArgs());
        startingBalance = Math.Round(Order2GoAddIn.FXCoreWrapper.GetAccount().Equity * (1 + PriceToAdd / 100), 0);
        ruleToExit = "0";
      }

    }
    void ShowSummary(Order2GoAddIn.Summary Summary, Order2GoAddIn.Account Account) {
      var summary = Summary ?? new Order2GoAddIn.Summary();
      double buyLossPerLotK;
      double sellLossPerLotK;
      sellPL = summary.SellNetPL;
      sellLots = summary.SellLots;
      sellLPP = summary.SellLPP;
      sellPos = summary.SellPositions;
      sellPips = (int)(summary.SellDelta / summary.PointSize);
      buyPL = summary.BuyNetPL;
      buyLots = summary.BuyLots;
      buyLPP = summary.BuyLPP;
      buyPos = summary.BuyPositions;
      buyPips = (int)(summary.BuyDelta / summary.PointSize);
      var totalPips = (summary.BuyPriceFirst - summary.SellPriceFirst) / fw.PointSize;
      Lib.SetLabelText(lblBuyPipsToNet, string.Format("{0:n1}", summary.BuyNetPLPip));
      Lib.SetLabelText(lblSellPipsToNet, string.Format("{0:n1}", summary.SellNetPLPip));
      buyLossPerLotK = summary.BuyLots > 0 ? summary.BuyNetPL / (summary.BuyLots / 1000) : 0;
      sellLossPerLotK = summary.SellLots > 0 ? summary.SellNetPL / (summary.SellLots / 1000) : 0;
    }
    public double DensityAverage { get { return Charting.DensityAverage; } }
    double _commission;
    public double Commission {
      get { return _commission; }
      set { _commission = value; PropertyChanged(this, new PropertyChangedEventArgs("Commission")); }
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
        PropertyChanged(this, new PropertyChangedEventArgs("DensityAverage"));
        if (Visibility == Visibility.Hidden) return;
        Order2GoAddIn.Price Price = fw.GetPrice();
        var digits = fw.Digits;
        var spread = ShowSpread(Price);

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
      chartingWindow.Show();
      chartingWindow.WindowState = WindowState.Normal;
      chartingWindow.fw_PriceChanged();
      corridorsWindow.Show();
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
        mw.AccountNumber = AccountNumber;
        app.MainWindows.Add(mw);
      }
      mw.Show();
    }
    App app { get { return Application.Current as App; } }


    #region INotifyPropertyChanged Members

    public event PropertyChangedEventHandler PropertyChanged;

    #endregion
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
