using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Bars;
using HedgeHog.Shared;

namespace IBApp {
  public class IBWraper : HedgeHog.Shared.ITradesManager {
    private readonly IBClientCore _ibClient;

    public IBWraper(ICoreFX coreFx) {
      CoreFX = coreFx;
      _ibClient = (IBClientCore)CoreFX;
    }

    #region ITradesManager - Implemented
    public Account GetAccount() {
      try {
        var trades = new Trade[] { };
        var account = new Account() {
          //ID = row.CellValue(FIELD_ACCOUNTID) + "",
          //Balance = (double)row.CellValue(FIELD_BALANCE),
          //UsableMargin = (double)row.CellValue(FIELD_USABLEMARGIN),
          //IsMarginCall = row.CellValue(FIELD_MARGINCALL) + "" == "W",
          //Equity = (double)row.CellValue(FIELD_EQUITY),
          //Hedging = row.CellValue("Hedging").ToString() == "Y",
          ////Trades = includeOtherInfo ? trades = GetTrades("") : null,
          ////StopAmount = includeOtherInfo ? trades.Sum(t => t.StopAmount) : 0,
          ////LimitAmount = includeOtherInfo ? trades.Sum(t => t.LimitAmount) : 0,
          //ServerTime = ServerTime
        };
        return account;
      } catch(Exception exc) {
        RaiseError(exc);
        return null;
      }
    }
    #region Error Event
    event EventHandler<ErrorEventArgs> ErrorEvent;
    public event EventHandler<ErrorEventArgs>  Error {
      add {
        if (ErrorEvent == null || !ErrorEvent.GetInvocationList().Contains(value))
          ErrorEvent += value;
      }
      remove {
        ErrorEvent -= value;
      }
    }
    protected void RaiseError(Exception exc) {
      if(ErrorEvent != null)
        ErrorEvent(this, new ErrorEventArgs(exc));
    }
    #endregion
    public DateTime ServerTime {
      get {
        return DateTime.Now + _ibClient._serverTimeOffset;
      }
    }
    #endregion

    public Func<Trade, double> CommissionByTrade {
      get {
        throw new NotImplementedException();
      }
    }

    public ICoreFX CoreFX { get; set; }

    public bool IsHedged {
      get {
        throw new NotImplementedException();
      }
    }

    public bool IsInTest {
      get {
        throw new NotImplementedException();
      }

      set {
        throw new NotImplementedException();
      }
    }

    public bool IsLoggedIn {
      get {
        throw new NotImplementedException();
      }
    }

    public double PipsToMarginCall {
      get {
        throw new NotImplementedException();
      }
    }

    public event EventHandler<OrderEventArgs> OrderAdded;
    public event EventHandler<OrderEventArgs> OrderChanged;
    public event OrderRemovedEventHandler OrderRemoved;
    public event EventHandler<PriceChangedEventArgs> PriceChanged;
    public event EventHandler<RequestEventArgs> RequestFailed;
    public event EventHandler<TradeEventArgs> TradeAdded;
    public event EventHandler<TradeEventArgs> TradeClosed;
    public event TradeRemovedEventHandler TradeRemoved;

    public void ChangeEntryOrderLot(string orderId, int lot) {
      throw new NotImplementedException();
    }

    public void ChangeEntryOrderPeggedLimit(string orderId, double rate) {
      throw new NotImplementedException();
    }

    public void ChangeEntryOrderPeggedStop(string orderId, double rate) {
      throw new NotImplementedException();
    }

    public void ChangeEntryOrderRate(string orderId, double rate) {
      throw new NotImplementedException();
    }

    public void ChangeOrderAmount(string orderId, int lot) {
      throw new NotImplementedException();
    }

    public void ChangeOrderRate(Order order, double rate) {
      throw new NotImplementedException();
    }

    public void CloseAllTrades() {
      throw new NotImplementedException();
    }

    public bool ClosePair(string pair) {
      throw new NotImplementedException();
    }

    public bool ClosePair(string pair, bool isBuy) {
      throw new NotImplementedException();
    }

    public bool ClosePair(string pair, bool isBuy, int lot) {
      throw new NotImplementedException();
    }

    public void CloseTrade(Trade trade) {
      throw new NotImplementedException();
    }

    public bool CloseTrade(Trade trade, int lot, Price price) {
      throw new NotImplementedException();
    }

    public void CloseTradeAsync(Trade trade) {
      throw new NotImplementedException();
    }

    public void CloseTradesAsync(Trade[] trades) {
      throw new NotImplementedException();
    }

    public double CommissionByTrades(params Trade[] trades) {
      throw new NotImplementedException();
    }

    public string CreateEntryOrder(string pair, bool isBuy, int amount, double rate, double stop, double limit) {
      throw new NotImplementedException();
    }

    public void DeleteEntryOrderLimit(string orderId) {
      throw new NotImplementedException();
    }

    public void DeleteEntryOrderStop(string orderId) {
      throw new NotImplementedException();
    }

    public bool DeleteOrder(string orderId) {
      throw new NotImplementedException();
    }

    public void DeleteOrders(string pair) {
      throw new NotImplementedException();
    }

    public PendingOrder FixCreateLimit(string tradeId, double limit, string remark) {
      throw new NotImplementedException();
    }

    public object FixOrderClose(string tradeId) {
      throw new NotImplementedException();
    }

    public object FixOrderClose(string tradeId, int mode, Price price, int lot) {
      throw new NotImplementedException();
    }

    public object[] FixOrdersClose(params string[] tradeIds) {
      throw new NotImplementedException();
    }

    public void FixOrderSetLimit(string tradeId, double takeProfit, string remark) {
      throw new NotImplementedException();
    }

    public void FixOrderSetStop(string tradeId, double stopLoss, string remark) {
      throw new NotImplementedException();
    }

    public void GetBars(string pair, int periodMinutes, int periodsBack, DateTime startDate, DateTime endDate, List<Rate> ratesList, bool doTrim, Func<List<Rate>, List<Rate>> map) {
      throw new NotImplementedException();
    }

    public void GetBars(string pair, int Period, int periodsBack, DateTime StartDate, DateTime EndDate, List<Rate> Bars, Action<RateLoadingCallbackArgs<Rate>> callBack, bool doTrim, Func<List<Rate>, List<Rate>> map) {
      throw new NotImplementedException();
    }

    public void GetBarsBase<TBar>(string pair, int period, int periodsBack, DateTime startDate, DateTime endDate, List<TBar> ticks, Func<List<TBar>, List<TBar>> map, Action<RateLoadingCallbackArgs<TBar>> callBack = null) where TBar : Rate {
      throw new NotImplementedException();
    }

    public IList<Rate> GetBarsFromHistory(string pair, int periodMinutes, DateTime dateTime, DateTime endDate) {
      throw new NotImplementedException();
    }

    public int GetBaseUnitSize(string pair) {
      throw new NotImplementedException();
    }

    public Trade[] GetClosedTrades(string pair) {
      throw new NotImplementedException();
    }

    public int GetDigits(string pair) {
      throw new NotImplementedException();
    }

    public Trade GetLastTrade(string pair) {
      throw new NotImplementedException();
    }

    public Order GetNetLimitOrder(Trade trade, bool getFromInternal = false) {
      throw new NotImplementedException();
    }

    public double GetNetOrderRate(string pair, bool isStop, bool getFromInternal = false) {
      throw new NotImplementedException();
    }

    public Offer GetOffer(string pair) {
      throw new NotImplementedException();
    }

    public Offer[] GetOffers() {
      throw new NotImplementedException();
    }

    public Order[] GetOrders(string pair) {
      throw new NotImplementedException();
    }

    public double GetPipCost(string pair) {
      throw new NotImplementedException();
    }

    public double GetPipSize(string pair) {
      throw new NotImplementedException();
    }

    public Price GetPrice(string pair) {
      throw new NotImplementedException();
    }

    public Tick[] GetTicks(string pair, int periodsBack, Func<List<Tick>, List<Tick>> map) {
      throw new NotImplementedException();
    }

    public Trade[] GetTrades() {
      throw new NotImplementedException();
    }

    public Trade[] GetTrades(string pair) {
      throw new NotImplementedException();
    }

    public Trade[] GetTradesInternal(string Pair) {
      throw new NotImplementedException();
    }

    public double InPips(string pair, double? price) {
      throw new NotImplementedException();
    }

    public double InPoints(string pair, double? price) {
      throw new NotImplementedException();
    }

    public double Leverage(string pair) {
      throw new NotImplementedException();
    }

    public PendingOrder OpenTrade(string Pair, bool isBuy, int lot, double takeProfit, double stopLoss, double rate, string comment) {
      throw new NotImplementedException();
    }

    public PendingOrder OpenTrade(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark, Price price) {
      throw new NotImplementedException();
    }

    public void RaisePriceChanged(string pair, Price price) {
      throw new NotImplementedException();
    }

    public void RaisePriceChanged(string pair, int barPeriod, Price price) {
      throw new NotImplementedException();
    }

    public double RateForPipAmount(Price price) {
      throw new NotImplementedException();
    }

    public double RateForPipAmount(double ask, double bid) {
      throw new NotImplementedException();
    }

    public void RefreshOrders() {
      throw new NotImplementedException();
    }

    public void ResetClosedTrades(string pair) {
      throw new NotImplementedException();
    }

    public double Round(string pair, double value, int digitOffset = 0) {
      throw new NotImplementedException();
    }

    public void SetServerTime(DateTime serverTime) {
      throw new NotImplementedException();
    }

    public Trade TradeFactory(string pair) {
      throw new NotImplementedException();
    }
  }
}