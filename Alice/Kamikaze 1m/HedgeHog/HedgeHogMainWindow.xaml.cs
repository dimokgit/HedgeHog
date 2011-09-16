﻿using System;
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
using Gala = GalaSoft.MvvmLight.Command;
using Order2GoAddIn;
using FXW = Order2GoAddIn.FXCoreWrapper;
using HedgeHog;
using HedgeHog.Models;
using HedgeHog.Shared;
using HedgeHog.Bars;
namespace HedgeHog {
  public partial class HedgeHogMainWindow : WindowModel, Wcf.ITraderServer, INotifyPropertyChanged, WpfPersist.IUserSettingsStorage {

    #region IUserSettingsStorage Members
    public WpfPersist.SaveDelegate Save { get; set; }
    #endregion
    #region Properties

    Thread threadCloseBuy = new Thread(() => { });
    Thread threadCloseSell = new Thread(() => { });

    public string _txtPassword, _txtAccNum;
    public double _txtTradeDelta, _txtStartingBalance;

    public string title { get { return AccountNumber + ":" + pair + ":" + app.WcfTraderAddress; } }
    public string AccountNumber {
      get { return _txtAccNum; }
      set { _txtAccNum = value; RaisePropertyChangedCore(); }
    }
    string _password;

    public string Password {
      get { return _password; }
      set { _password = value; RaisePropertyChangedCore(); }
    }

    public ComboBoxItem LotsToTradeBuy { get; set; }
    int lotsToBuyRatio { get { return Convert.ToInt32(LotsToTradeBuy.Content); } }

    public ComboBoxItem LotsToTradeSell { get; set; }
    int lotsToSellRatio { get { return Convert.ToInt32(LotsToTradeSell.Content); } }

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

    enum Condition { None, LessThen, MoreThen };
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

    private Thread threadProc;
    private Thread threadWait;

    string logFileName = "Log.txt";
    static readonly int logQueueLength = 10;
    Queue<string> logQueue = new Queue<string>(logQueueLength);
    object Log {
      set {
        var exc = value as Exception;
        var message = exc == null ? value + "" : exc.Message;
        if (logQueue.Count > logQueueLength) logQueue.Dequeue();
        var time = DateTime.Now.ToString("[dd HH:mm:ss] ");
        logQueue.Enqueue(time + message);
        txtAccNum.Dispatcher.BeginInvoke(new Action(delegate() {
          txtLog.Text = string.Join(Environment.NewLine, logQueue.Reverse());
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
        if (value > 0) {
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
      set { _sellPL = value; RaisePropertyChangedCore(); }
    }
    double _sellLPP;
    public double SellLPP {
      get { return _sellLPP; }
      set { _sellLPP = value; RaisePropertyChangedCore(); }
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
      set { _sellLots = value; RaisePropertyChangedCore(); }
    }
    double _buyPL;
    public double BuyPL {
      get { return _buyPL; }
      set { _buyPL = value; RaisePropertyChangedCore(); }
    }
    double _buyLPP;
    public double BuyLPP {
      get { return _buyLPP; }
      set { _buyLPP = value; RaisePropertyChangedCore(); }
    }
    double _buyPos;
    public double BuyPositions {
      get { return _buyPos; }
      set { _buyPos = value; RaisePropertyChangedCore(); }
    }
    int _buyPips;
    public int BuyPips {
      get { return _buyPips; }
      set { _buyPips = value; RaisePropertyChangedCore(); }
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

    #region

    ICommand _OpenNewAccountCommand;
    public ICommand OpenNewAccountCommand {
      get {
        if (_OpenNewAccountCommand == null) {
          _OpenNewAccountCommand = new Gala.RelayCommand(OpenNewAccount, () => true);
        }

        return _OpenNewAccountCommand;
      }
    }
    public void OpenNewAccount(string account, string password) {
      if (!isDemo) throw new NotSupportedException("Only demo accounts can be replaced with new account.");
      AccountNumber = account;
      Password = password;
      if (fw != null && fw.Desk != null) {
        Dispatcher.BeginInvoke(new Action(() => {
          fw.LogOff();
          Login();
        }));
      }
    }
    public void OpenNewAccount() {
      string account, pwd;
      FXCM.Lib.GetNewAccount(out account, out pwd);
      OpenNewAccount(account, pwd);
      MessageBox.Show("Account updated.");
    }

    #endregion

    Order2GoAddIn.FXCoreWrapper fw;
    public HedgeHogMainWindow() : this("") { }
    public HedgeHogMainWindow(string name) {
      if (name + "" != "") this.Name = name;
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

    ICommand _LoginCommand;
    public ICommand LoginCommand {
      get {
        if (_LoginCommand == null) {
          _LoginCommand = new Gala.RelayCommand(Login, () => true);
        }

        return _LoginCommand;
      }
    }

    private void Login() {
      chartingWindow.Show();
      corridorsWindow.Show();
      Dispatcher.BeginInvoke(new Action(() => {
        try {
          if (app.FXCM.LogOn(AccountNumber, Password, Properties.Settings.Default.ServerUrl, isDemo)) {
            fw.Pair = pair;
            Leverage = fw.Leverage();
            chartingWindow.Dispatcher.BeginInvoke(new Action(() => {
              chartingWindow.ProcessPrice(null);
            }));
          }
        } catch (Exception exc) {
          System.Windows.MessageBox.Show(exc.Message);
        }
      }));
    }

    #region Event Handlers

    double _leverage;

    public double Leverage {
      get { return _leverage; }
      set {
        _leverage = value;
        RaisePropertyChangedCore();
      }
    }

    private void OnPairChanged(object sender, SelectionChangedEventArgs e) {
      if (fw != null && fw.IsLoggedIn) {
          fw.Pair = pair;
          dataGrid1.ItemsSource = fw.GetTrades().ToList();
          Leverage = fw.Leverage();
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
        if (fw != null) {
          app.FXCM.Logout();
        }
          if (chartingWindow != null)
            chartingWindow.Close();
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
        fw = new Order2GoAddIn.FXCoreWrapper(app.FXCM);
        chartingWindow = new HedgeHog.Charting(this.Name, fw);
        corridorsWindow = new Corridors(this.Name);
        chartingWindow.TicksChanged += chartingWindow_TicksChanged;
        chartingWindow.PriceGridChanged += ProcessPrice;
        chartingWindow.PriceGridError += chartingWindow_PriceGridError;
        dataGrid1.AutoGeneratingColumn += new EventHandler<Microsoft.Windows.Controls.DataGridAutoGeneratingColumnEventArgs>(dataGrid1_AutoGeneratingColumn);
        RaisePropertyChanged(() => title);
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

    void ShowSpread(Price Price) {
      var digits = fw.GetDigits(pair);
      Spread = fw.InPips(Price.Ask - Price.Bid);
      SpreadCma = SpreadCma.Cma(50, Spread);
    }

    void app_ClosingBalanceChanged(object sender, ClosingBalanceChangedEventArgs e) {
      if (sender != this && StartingBalance > 0 && StartingBalance < e.ClosingBalance) StartingBalance = e.ClosingBalance;
    }

    void ShowAccount(Account Account, Order2GoAddIn.Summary Summary) {
      AccountBalance = Account.Balance;
      PipsToMC = Account.PipsToMC;
      minEquityHistory = (int)Account.Equity;
      LotsLeft = (int)(Account.UsableMargin * Leverage);
      var tradesAll = fw.GetTrades("");
      NetPL = tradesAll.GrossInPips();
      UsableMargin = string.Format("{0:c0}/{1:p1}", Account.UsableMargin, Account.UsableMargin / Account.Equity);
      AccountEquity = Account.Equity;// string.Format("{0:c0}/{1:n1}", Account.Equity, netPL);
      var doCloseLotsOfTrades = tradesAll.Length > app.MainWindows.Count + 1 && Account.Gross > 0;
      Commission = fw.CommisionPending;
      var haveGoodProfit = DensityAverage>0 && NetPL.Abs() >= DensityAverage;
      if (StartingBalance > 0 && Account.Equity >= StartingBalance ||
        haveGoodProfit ||
        doCloseLotsOfTrades ||
        (priceToExit > 0 &&
        ((conditionToExit == Condition.LessThen && Summary.PriceCurrent.Average < priceToExit) ||
          (conditionToExit == Condition.MoreThen && Summary.PriceCurrent.Average > priceToExit)
        ))
        ) {
        ClosePositions(this, new RoutedEventArgs());
        StartingBalance = Math.Round(fw.GetAccount().Equity * (1 + PriceToAdd / 100), 0);
        app.RaiseClosingalanceChanged(this, StartingBalance.ToInt());
        RuleToExit = "0";
      }

    }
    void ShowSummary(Order2GoAddIn.Summary Summary, Account Account) {
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
      var totalPips = (summary.BuyPriceFirst - summary.SellPriceFirst) / fw.GetPipSize(pair);
      BuyPipsToNet = summary.BuyNetPLPip;
      SellPipsToNet = summary.SellNetPLPip;
      buyLossPerLotK = summary.BuyLots > 0 ? summary.BuyNetPL / (summary.BuyLots / 1000) : 0;
      sellLossPerLotK = summary.SellLots > 0 ? summary.SellNetPL / (summary.SellLots / 1000) : 0;
    }
    public double DensityAverage { get { return Charting.DensityAverage; } }
    double _commission;
    public double Commission {
      get { return _commission; }
      set { _commission = value; RaisePropertyChangedCore(); }
    }
    void fw_PriceChanged(Price Price) {
      if (threadProc != null && threadProc.ThreadState == ThreadState.Running) {
        if (threadWait != null && threadWait.ThreadState == ThreadState.Running) threadWait.Abort();
        threadWait = new Thread(delegate() {
          threadProc.Join();
          threadProc = new Thread(delegate() { ProcessPrice(Price); });
          threadProc.Priority = ThreadPriority.Lowest;
          try {
            threadProc.Start();
          } catch (ThreadStateException) { }
        });
        threadWait.Start();
      } else {
        threadProc = new Thread(delegate() { ProcessPrice(Price); });
        threadProc.Priority = ThreadPriority.Lowest;
        threadProc.Start();
      }
    }
    private void ProcessPrice() { ProcessPrice(fw.GetPrice()); }
    private void ProcessPrice(Price Price) {
      try {
        RaisePropertyChanged(() => DensityAverage);
        if (Visibility == Visibility.Hidden) return;
        var digits = fw.GetDigits(pair);
        ShowSpread(Price);

        var account = fw.GetAccount();
        var usableMargin = account.UsableMargin;
        var avalibleTotal = usableMargin * Leverage;
        AmountToBuy = TradesManagerStatic.GetLotstoTrade(account.Balance, Leverage, lotsToBuyRatio, fw.MinimumQuantity);
        AmountToSell = TradesManagerStatic.GetLotstoTrade(account.Balance, Leverage, lotsToSellRatio, fw.MinimumQuantity);
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
              var lots = chartingWindow.LotsToTradeBuy > 1000 ? chartingWindow.LotsToTradeBuy : chartingWindow.LotsToTradeBuy * AmountToBuy;
              var l = fw.CanTrade2(true, buyTradeDelta, lots, tradeDistanceUnisex);
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
              var lots = chartingWindow.LotsToTradeSell > 1000 ? chartingWindow.LotsToTradeSell : chartingWindow.LotsToTradeSell * AmountToSell;
              var l = fw.CanTrade2(false, sellTradeDelta, lots, tradeDistanceUnisex);
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
      } catch (ThreadAbortException) {
      } catch (Exception exc) {
        Log = exc;
      }
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
        fw.ClosePositions();
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

    #region ITraderService Members

    public string GetData(int value) {
      throw new NotImplementedException();
    }

    public Alice.WCF.CompositeType GetDataUsingDataContract(Alice.WCF.CompositeType composite) {
      throw new NotImplementedException();
    }

    public Account GetAccount() {
      var account = fw.GetAccount();
      account.Orders = fw.GetEntryOrders("");
      return account;
    }

    public string CloseTrade(string tradeID) {
      return fw.FixOrderClose(tradeID) + "";
    }

    #endregion

    #region ITraderServer Members


    public string[] CloseTrades(string[] tradeIds) {
      return fw.FixOrdersClose(tradeIds).Select(o => o + "").ToArray();
    }

    public string[] CloseAllTrades() {
      return fw.FixOrdersCloseAll().Select(o => o + "").ToArray();
    }

    #endregion

    private int amountToSell;
    public int AmountToSell {
      get { return amountToSell; }
      set { amountToSell = value; RaisePropertyChanged(() => LotsToSell); }
    }

    int amountToBuy;
    public int AmountToBuy {
      get { return amountToBuy; }
      set { amountToBuy = value; RaisePropertyChanged(() => LotsToBuy); }
    }
    public int LotsToBuy { get { return fw == null || !fw.IsLoggedIn ? 0 : AmountToBuy / fw.MinimumQuantity; } }
    public int LotsToSell { get { return fw == null || !fw.IsLoggedIn ? 0 : AmountToSell / fw.MinimumQuantity; } }
  }
  public class ToolTips {
    public string CurrentDirectory { get { return AppDomain.CurrentDomain.BaseDirectory; } }
  }
}
