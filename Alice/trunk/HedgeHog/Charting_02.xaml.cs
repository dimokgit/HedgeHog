using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using TA = TicTacTec.TA.Library.Core;

namespace HedgeHog {
  /// <summary>
  /// Interaction logic for Charting.xaml
  /// </summary>
  public partial class Charting : Window {
    enum BarsPeriodType { t1 = 0, m1 = 1, m5 = 5, m15 = 15, m30 = 30, H1 = 60,D1 = 24,W1 = 7, M1 = 12 }
    public bool GoBuy;
    public bool GoSell;
    public bool CloseBuy;
    public bool CloseSell;
    public double StopLossBuy;
    public double StopLossSell;
    public double TakeProfitBuy;
    public double TakeProfitSell;
    public int LotsToTrade;

    private double stopLossAddOn { get { return Lib.GetTextBoxTextDouble(txtStopLossAddOn); } }
    private double edgeMargin { get { return Lib.GetTextBoxTextDouble(txtEdgeMargin); } }
    private BarsPeriodType bsPeriodMin { get { return (BarsPeriodType)Lib.GetTextBoxTextInt(txtBSPeriodMin); } }
    private int bsPeriodMax { get { return Lib.GetTextBoxTextInt(txtBSPeriodMax); } }
    private bool useMaxForSpreadCalc { get { return false/*chkMax.IsChecked.Value*/; } }
    private bool closeOnReverseOnly { get { return Properties.Settings.Default.CloseOnReverse; } }
    private Thread threadProc;

    Order2GoAddIn.FXCoreWrapper fw { get; set; }
    string pair { get { return fw.Pair; } }
    double timeSpanLast = 0;
    double? positionBuy_Locked = null;
    double? positionSell_Locked = null;

    public delegate void PriceGridErrorHandler(Exception exc);
    public event PriceGridErrorHandler PriceGridError;

    public delegate void PriceGridChangedHandler(Order2GoAddIn.Price Price);
    public event PriceGridChangedHandler PriceGridChanged;

    //bool isMarketMakerMode { get { return chkMarketMakerMode.IsChecked.Value; } }
    public Charting(Order2GoAddIn.FXCoreWrapper FW) {
      InitializeComponent();
      fw = FW;
      fw.PriceChanged += new Order2GoAddIn.FXCoreWrapper.PriceChangedHandler(fw_PriceChanged);
    }

    public double TakeProfitNet(double TakePropfitPips,Order2GoAddIn.Summary Summary,bool Buy) {
      return Math.Round(
        TakePropfitPips == 0 ? 0 :
        Buy ? Summary.BuyAvgOpen + TakePropfitPips * fw.PointSize : Summary.SellAvgOpen - TakePropfitPips * fw.PointSize, fw.Digits);
    }
    delegate void treadStarter();
    Action runPrice = null;
    IAsyncResult asyncRes = null;
    private ThreadStart priceChangedStarter;
    private void fw_PriceChanged(Order2GoAddIn.Price Price) {
      this.priceChangedStarter = null;
      ThreadState[] ts = new[] { ThreadState.WaitSleepJoin, ThreadState.Running };
      if ((this.threadProc != null) && ts.Contains<ThreadState>(this.threadProc.ThreadState)) {
        this.priceChangedStarter = delegate() { this.ProcessPrice(Price); };
      } else {
        this.threadProc = new Thread(delegate() {
          this.ProcessPrice(Price);
          ThreadStart pcs = this.priceChangedStarter;
          if (pcs != null) {
            new Thread(pcs) { Priority = ThreadPriority.AboveNormal }.Start();
          }
        });
        this.threadProc.Start();
      }
    }

 

 

    void fw_PriceChanged_(Order2GoAddIn.Price Price) {
      runPrice = delegate() { ProcessPrice(Price); };
      if (asyncRes == null || asyncRes.IsCompleted) {
        asyncRes = runPrice.BeginInvoke(priceCallBack, null);
        runPrice = null;
      }
    }
    void priceCallBack(IAsyncResult res) {
      if (runPrice != null) {
        asyncRes = runPrice.BeginInvoke(priceCallBack, null);
        runPrice = null;
      }
    }
    void Charting_priceChangedStarter() {
      throw new NotImplementedException();
    }
    public class PriceBar {
      public double AskHigh { get; set; }
      public double AskLow { get; set; }
      public double BidLow { get; set; }
      public double BidHigh { get; set; }
      public double Spread { get; set; }
      public double Speed { get; set; }
      public double Power { get; set; }
      public double Minutes { get; set; }
      public DateTime StartDate { get; set; }
    }
    ObservableCollection<Rate> BarsDataSource = new ObservableCollection<Rate>();
    class Rate {
      public double AskClose { get; set; }
      public double AskHigh { get; set; }
      public double AskLow { get; set; }
      public double AskOpen { get; set; }
      public double BidClose { get; set; }
      public double BidHigh { get; set; }
      public double BidLow { get; set; }
      public double BidOpen { get; set; }
      public DateTime StartDate { get; set; }
      public double Minutes { get; set; }
    }
    
    [MethodImpl(MethodImplOptions.Synchronized)]
    void ProcessPrice(Order2GoAddIn.Price Price) {
      try {
        DateTime serverTime = ((DateTime)fw.Desk.ServerTime).AddHours(-4);
        var timeNow = DateTime.Now;
        Rate[] rates;
        lock (fw.DeskLocker) {
          Price = fw.GetPrice();
          FXCore.MarketRateEnumAut mr = (FXCore.MarketRateEnumAut)fw.Desk.GetPriceHistory(pair, bsPeriodMin + "", DateTime.Now.AddMinutes(-(bsPeriodMax + 2)), DateTime.FromOADate(0), bsPeriodMax, true, true);
          DateTime firstBarDate = serverTime;
          rates = mr.OfType<FXCore.MarketRateAut>()
            .OrderByDescending(r => r.StartDate)
            .Select((r, i) => new Rate() {
              AskClose = r.AskClose, AskHigh = r.AskHigh, AskLow = r.AskLow, AskOpen = r.AskOpen,
              BidClose = r.BidClose, BidHigh = r.BidHigh, BidLow = r.BidLow, BidOpen = r.BidOpen,
              StartDate = i == 0 ? (firstBarDate = r.StartDate) : r.StartDate,
              //Minutes = (serverTime - firstBarDate).TotalMinutes + i * (int)bsPeriodMin
              Minutes = (serverTime - r.StartDate).TotalMinutes
            }).ToArray();
        }
        var ratePrev = rates.Skip(1).FirstOrDefault();
        var rateCurr = rates.FirstOrDefault();
        if (ratePrev != null) {

          #region bsPeriods
          double askMax = double.MinValue;
          double askMin = double.MaxValue;
          double bidMax = double.MinValue;
          double bidMin = double.MaxValue;
          var aBars = rates.ToArray();
          var bsPeriods = aBars
            .Select(r => new {
              AskHigh = (askMax = Math.Round(Math.Max(askMax, r.AskHigh), fw.Digits)),
              AskLow = (askMin = Math.Round(Math.Min(askMin, r.AskLow), fw.Digits)),
              BidLow = (bidMin = Math.Round(Math.Min(bidMin, r.BidLow), fw.Digits)),
              BidHigh = (bidMax = Math.Round(Math.Max(bidMax, r.BidHigh), fw.Digits)),
              SpreadAsk = askMax - askMin,
              SpreadBid = bidMax - bidMin,
              SpeedAsk = (askMax - askMin) / r.Minutes,
              SpeedBid = (bidMax - bidMin) / r.Minutes,
              Minutes = Math.Round(r.Minutes, 1),
              StartDate = r.StartDate
            }
              )
            //.Where(r => r.Minutes >= bsPeriodMin)
            .Select(r => new {
              AskHigh = r.AskHigh,
              AskLow = r.AskLow,
              BidLow = r.BidLow,
              BidHigh = r.BidHigh,
              Spread = Math.Round(Math.Min(r.SpreadAsk, r.SpreadBid) / fw.PointSize, 1),
              Speed = Math.Round((useMaxForSpreadCalc ? Math.Max(r.SpeedAsk, r.SpeedBid) : Math.Min(r.SpeedAsk, r.SpeedBid)) / fw.PointSize, 2),
              Power = Math.Round(
                (useMaxForSpreadCalc ? Math.Max(r.SpeedAsk, r.SpeedBid) : Math.Min(r.SpeedAsk, r.SpeedBid)) *
                (useMaxForSpreadCalc ? Math.Max(r.SpreadAsk, r.SpreadBid) : Math.Max(r.SpreadAsk, r.SpreadBid)) / fw.PointSize / fw.PointSize, 2
              ),
              Minutes = r.Minutes,
              StartDate = r.StartDate
            })
            .Where(r => r.Minutes >= (int)bsPeriodMin)
            .OrderByDescending(r => r.Power)
            .ThenByDescending(r => r.Spread)
            .ThenBy(r => r.Minutes);
            
          #endregion

          timeSpanLast = DateTime.Now.Subtract(timeNow).TotalMilliseconds;

          dgBuySellBars.Dispatcher.BeginInvoke(
            new Action(delegate() {
            dgBuySellBars.ItemsSource = bsPeriods.Take((int)(bsPeriodMax*.15)).ToArray();
          })
          );
          var barsBest = bsPeriods.First();

          StopLossBuy = stopLossAddOn == 0 ? 0 : -(barsBest.BidLow - barsBest.Spread * stopLossAddOn * fw.PointSize);
          StopLossSell = stopLossAddOn == 0 ? 0 : -(barsBest.AskHigh + barsBest.Spread * stopLossAddOn * fw.PointSize);

          #region tickList
          List<string> tick = new List<string>();
          tick.Add(Price.Time.ToString("dd/MM/yyyy hh:mm:ss"));
          tick.Add(((Price.Ask + Price.Bid) / 2).ToString("n" + fw.Digits));
          tick.Add(barsBest.AskHigh + "");
          tick.Add(barsBest.AskLow + "");
          tick.Add(barsBest.BidLow + "");
          tick.Add(barsBest.BidHigh + "");
          tick.Add(barsBest.Spread + "");
          tick.Add(barsBest.Spread + "");
          tick.Add(barsBest.Power + "");
          tick.Add(barsBest.Minutes + "");
          //System.IO.File.AppendAllText("Ticks.csv", string.Join(",", tick.ToArray())+Environment.NewLine);
          #endregion

          double dg = Math.Pow(10, fw.Digits);
          double powerCurrent = barsBest.Power;
          LotsToTrade = 1;// Math.Max(1, (int)Math.Round(Math.Log10(powerCurrent), 0));
          double powerAverage = bsPeriods.Average(r => r.Power);
          Lib.SetLabelText(lblPower, string.Format("{0:n1}/{1:n1}={2:n2}"
            , powerCurrent, powerAverage, powerCurrent / powerAverage));

          double bidLowBig = barsBest.BidLow;
          double askHighBig = barsBest.AskHigh;

          var summary = fw.GetSummary();

          var positionBuy = (Price.Ask - barsBest.AskLow) / (barsBest.AskHigh - barsBest.AskLow);
          var goBuy = (positionBuy_Locked ?? positionBuy) <= edgeMargin;
          GoBuy = goBuy && Price.Ask > ratePrev.AskClose;
          CloseSell = !closeOnReverseOnly || (summary != null && summary.SellNetPL > 0) ? goBuy : GoBuy;
          //positionBuy_Locked = goBuy && !GoBuy ? (positionBuy_Locked ?? positionBuy) : (double?)null;
          TakeProfitBuy = 0;// -(barsBest.AskLow + barsBest.Spread * edgeMargin * 2);
          Lib.SetLabelText(lblOpenBuy, string.Format("{0:p0}", positionBuy));

          var positionSell = (barsBest.BidHigh - Price.Bid) / (barsBest.BidHigh - barsBest.BidLow);
          var goSell = (positionSell_Locked ?? positionSell) <= edgeMargin;
          GoSell = goSell && Price.Bid < ratePrev.BidClose;
          CloseBuy = !closeOnReverseOnly || (summary != null && summary.BuyNetPL>0) ? goSell : GoSell;
          //positionSell_Locked = goSell && !GoSell? (positionSell_Locked ?? positionSell) : (double?)null;
          TakeProfitSell = 0;// -(barsBest.BidHigh - barsBest.Spread * edgeMargin * 2);
          Lib.SetLabelText(lblOpenSell, string.Format("{0:p0}", positionSell));

          Lib.SetBackGround(lblOpenSell, new SolidColorBrush(
            GoSell ? Colors.PaleGreen : CloseSell ? Colors.LightSalmon : goSell ? Colors.Yellow : Colors.Transparent)
          );
          Lib.SetBackGround(lblOpenBuy, new SolidColorBrush(
            GoBuy ? Colors.PaleGreen : CloseBuy ? Colors.LightSalmon : goBuy ? Colors.Yellow : Colors.Transparent)
          );

          if (PriceGridChanged != null) PriceGridChanged(Price);
          Lib.SetLabelText(lblServerTime, string.Format("{0:HH:mm:ss}/{1:n0}ms[{2:n3}]", serverTime, timeSpanLast, Price.Ask));
        }
      } catch (ThreadAbortException) {
      } catch (Exception exc) {
        if (PriceGridError != null) PriceGridError(exc);
      }
    }
    int GetAvailableThreads() {
      int wt, cpt;
      ThreadPool.GetAvailableThreads(out wt, out cpt);
      return wt;
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
  }
}
