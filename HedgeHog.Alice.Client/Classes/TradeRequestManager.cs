using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FXW = Order2GoAddIn.FXCoreWrapper;
using System.Threading;
using HedgeHog.Alice.Client.TradeExtenssions;
using HedgeHog.Shared;


namespace HedgeHog.Alice.Client {
  class TradeRequestManager {
    #region Event
    public event EventHandler<TradeRequestManagerEventArgs> TradeRequestManagerEvent;
    protected virtual void RaiseTradeRequestManagerEvent(Exception exc) {
      if (TradeRequestManagerEvent != null)
        TradeRequestManagerEvent(this, new TradeRequestManagerEventArgs(exc));
    }
    #endregion

    #region Ctor
    ThreadScheduler openQueueScheduler;
    public FXW fw { get; set; }
    public TradeRequestManager(FXW fw) {
      this.fw = fw;
      openQueueScheduler = new ThreadScheduler((s, e) => RaiseTradeRequestManagerEvent(e.Exception));
    }
    #endregion

    #region OpenRequest
    class OpenRequest:IEquatable<OpenRequest> {
      public string pair;
      public bool buy;
      public int lots;
      public string serverTradeID;
      public Trade pendingTrade;
      public OpenRequest(string pair, bool buy, int lots, string serverTradeID,Trade pendingTrade) {
        this.pair = pair;
        this.buy = buy;
        this.lots = lots;
        this.serverTradeID = serverTradeID;
        this.pendingTrade = pendingTrade;
      }

      #region IEquatable<OpenRequest> Members

      public bool Equals(OpenRequest other) {
        return this.pair == other.pair && this.buy == other.buy && this.lots == other.lots && this.serverTradeID == other.serverTradeID;
      }
      public override bool Equals(Object obj) {
        if (obj == null) return base.Equals(obj);
        if (!(obj is OpenRequest))
          throw new InvalidCastException("The 'obj' argument is not an " + GetType().Name + " object.");
        else
          return Equals(obj as OpenRequest);
      }

      public override int GetHashCode() {
        return pair.GetHashCode() ^ buy.GetHashCode() ^ lots.GetHashCode() ^ serverTradeID.GetHashCode();
      }
      public static bool operator ==(OpenRequest or1, OpenRequest or2) {
        return or1.Equals(or2);
      }

      public static bool operator !=(OpenRequest or1, OpenRequest or2) {
        return (!or1.Equals(or2));
      }

      #endregion
    }
    #endregion

    Queue<OpenRequest> openQueue = new Queue<OpenRequest>();
    public void AddOpenTradeRequest(string pair, bool buy, int lots, string serverTradeID,Trade pendingTrade ) {
      var or = new OpenRequest(pair, buy, lots, serverTradeID,pendingTrade);
      if (openQueue.Contains(or)) return;
      if (fw.GetTrades("").Any(t => t.MasterTradeId() == serverTradeID)) return;
      openQueue.Enqueue(or);
      if (!openQueueScheduler.IsRunning)
        openQueueScheduler.Command = () => RunOpenQueue();
    }

    void RunOpenQueue() {
      while (openQueue.Count > 0) {
        var or = openQueue.Peek();
        OpenTrade(or.pair, or.buy, or.lots, or.serverTradeID, or.pendingTrade);
        openQueue.Dequeue();
      }
    }
    private void OpenTrade(string pair, bool buy, int lots, string serverTradeID,Trade pendingTrade) {
      try {
        string orderId = "", tradeId = "";
        while (true) {
          fw.FixOrderOpen(pair, buy, lots, 0, 0, serverTradeID, out orderId, out tradeId);
          if (string.IsNullOrWhiteSpace(orderId))
            GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher.Invoke(new Action(() => {
              pendingTrade.GetUnKnown().ErrorMessage = "Waiting";
              RaiseTradeRequestManagerEvent(
                new Exception("Waiting for previous pending order " + fw.GetOrders("").Select(o => o.OrderID).DefaultIfEmpty("XXXXX")));
              Thread.Sleep(10);
            }));
          else break;
        }
        while (!fw.GetTrades("").Any(t => t.OpenOrderID == orderId))
          GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher.Invoke(new Action(() => {
            pendingTrade.GetUnKnown().ErrorMessage = "Waiting";
            RaiseTradeRequestManagerEvent(new Exception("Waiting for order " + orderId));
            Thread.Sleep(10);
          }));
        tradeId = fw.GetTrades("").First(t => t.OpenOrderID == orderId).Id;
        RaiseTradeRequestManagerEvent(new Exception("Opened trade " + tradeId));
      } catch (Exception exc) {
        pendingTrade.GetUnKnown().ErrorMessage = exc.Message;
        RaiseTradeRequestManagerEvent(exc); 
      }
    }
  }

  #region TradeRequestManagerEventArgs
  public class TradeRequestManagerEventArgs : EventArgs {
    public Exception Exception { get; set; }
    public TradeRequestManagerEventArgs(Exception exception) {
      this.Exception = exception;
    }
  }
  #endregion
}
