/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IBSampleApp.util;
using static HedgeHog.Core.JsonExtensions;
using HedgeHog.Shared;
using IBApi;
using HedgeHog;
using System.Collections.Concurrent;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ReactiveUI;
using PosArg = System.Tuple<string, IBApi.Contract, IBApp.PositionMessage>;
namespace IBApp {
  public class AccountManager : DataManager {
    #region Constants
    private const int ACCOUNT_ID_BASE = 50000000;

    private const string ACCOUNT_SUMMARY_TAGS = "AccountType,NetLiquidation,TotalCashValue,SettledCash,AccruedCash,BuyingPower,EquityWithLoanValue,PreviousEquityWithLoanValue,"
             +"GrossPositionValue,ReqTEquity,ReqTMargin,SMA,InitMarginReq,MaintMarginReq,AvailableFunds,ExcessLiquidity,Cushion,FullInitMarginReq,FullMaintMarginReq,FullAvailableFunds,"
             +"FullExcessLiquidity,LookAheadNextChange,LookAheadInitMarginReq ,LookAheadMaintMarginReq,LookAheadAvailableFunds,LookAheadExcessLiquidity,HighestSeverity,DayTradesRemaining,Leverage";
    #endregion

    #region Fields
    private bool accountSummaryRequestActive = false;
    private bool accountUpdateRequestActive = false;
    private string _accountId;
    private readonly Action<object> _defaultMessageHandler;
    private readonly string _accountCurrency="USD";
    #endregion

    #region Properties
    public Account Account { get; private set; }
    private readonly ReactiveList<Trade> OpenTrades = new ReactiveList<Trade>();
    private readonly ConcurrentDictionary<string, PositionMessage> _positions = new ConcurrentDictionary<string, PositionMessage>();
    private readonly ReactiveList<Trade> ClosedTrades = new ReactiveUI.ReactiveList<Trade>();
    public Func<Trade, double> CommissionByTrade { get; private set; }
    #endregion

    #region Methods

    #endregion

    #region Ctor
    public AccountManager(IBClientCore ibClient, string accountId, Func<Trade, double> commissionByTrade, Action<object> onMessage) : base(ibClient, ACCOUNT_ID_BASE) {
      CommissionByTrade = commissionByTrade;
      Account = new Account();
      _accountId = accountId;
      _defaultMessageHandler = onMessage ?? new Action<object>(o => { throw new NotImplementedException(new { onMessage } + ""); });
      IbClient.AccountSummary += OnAccountSummary;
      IbClient.AccountSummaryEnd += OnAccountSummaryEnd;
      IbClient.UpdateAccountValue += OnUpdateAccountValue;
      IbClient.UpdatePortfolio += OnUpdatePortfolio;
      IbClient.ExecDetails += OnExecDetails;
      //IbClient.CommissionReport += OnExecDetails;

      IbClient.Position += OnPosition;
    }

    /*
    public bool CloseTrade(Trade trade, int lot, Price price) {
      if(trade.Lots <= lot)
        CloseTrade(trade);
      else {
        var newTrade = trade.Clone();
        newTrade.Lots = trade.Lots - lot;
        newTrade.Id = NewTradeId() + "";
        var e = new PriceChangedEventArgs(trade.Pair, price ?? GetPrice(trade.Pair), GetAccount(), GetTrades());
        newTrade.UpdateByPrice(this, e);
        trade.Lots = lot;
        trade.UpdateByPrice(this, e);
        CloseTrade(trade);
        tradesOpened.Add(newTrade);
      }
      return true;
    }
    */
    #endregion

    #region Events

    #region TradeAdded Event
    public class TradeEventArgs : EventArgs {
      public Trade Trade { get; private set; }
      public TradeEventArgs(Trade trade) : base() {
        Trade = trade;
      }
    }
    event EventHandler<TradeEventArgs> TradeAddedEvent;
    public event EventHandler<TradeEventArgs>  TradeAdded {
      add {
        if (TradeAddedEvent == null || !TradeAddedEvent.GetInvocationList().Contains(value))
          TradeAddedEvent += value;
      }
      remove {
        TradeAddedEvent -= value;
      }
    }
    protected void RaiseTradeAdded(Trade trade) {
      if(TradeAddedEvent != null)
        TradeAddedEvent(this, new TradeEventArgs(trade));
    }
    #endregion

    #region TradeRemoved Event
    event EventHandler<TradeEventArgs> TradeRemovedEvent;
    public event EventHandler<TradeEventArgs>  TradeRemoved {
      add {
        if (TradeRemovedEvent == null || !TradeRemovedEvent.GetInvocationList().Contains(value))
          TradeRemovedEvent += value;
      }
      remove {
        TradeRemovedEvent -= value;
      }
    }
    protected void RaiseTradeRemoved(Trade trade) {
      if(TradeRemovedEvent != null)
        TradeRemovedEvent(this, new TradeEventArgs(trade));
    }
    #endregion

    #endregion

    #region Trades
    public Trade[] GetTrades() { return OpenTrades.ToArray(); }
    #endregion
    public class PositionNotFoundException : Exception {
      public PositionNotFoundException(string symbol) : base(new { symbol } + "") { }
    }
    #region IB Handlers
    private void OnExecDetails(int reqId, Contract contract, Execution execution) {
      try {
        var symbol = contract.LocalSymbol;
        var execTime = execution.Time.FromTWSString();
        var execPrice = execution.AvgPrice;

        #region Create Trade
        var trade = Trade.Create<Trade>(symbol, TradesManagerStatic.GetPointSize(symbol), CommissionByTrade);
        trade.Id = execution.PermId + "";
        trade.Buy = execution.Side == "BOT";
        trade.IsBuy = trade.Buy;
        trade.Time2 = execTime;
        trade.Time2Close = execution.Time.FromTWSString();
        trade.Open = trade.Close = execPrice;
        trade.Lots = execution.CumQty;
        trade.OpenOrderID = execution.OrderId + "";
        #endregion

        PositionMessage position;
        if(!_positions.TryGetValue(contract.LocalSymbol, out position))
          throw new PositionNotFoundException(symbol);

        #region Close Trades
        OpenTrades
          .ToArray()
          .Where(IsNotEqual(trade))
          .Scan(0, (lots, otClose) => {
            var cL = CloseTrade(execTime, execPrice, otClose, trade.Lots);
            return trade.Lots = cL;
          })
          .TakeWhile(l => l > 0)
          .Count();
        #endregion

        if(trade.Lots > 0)
          OpenTrades.Add(trade);

        var openLots = OpenTrades
          .Where(IsEqual(position))
          .Sum(ot => ot.Lots);
        Passager.ThrowIf(() => openLots != position.Quantity);

        _defaultMessageHandler(new { symbol = contract.LocalSymbol + "", execution }.ToJson());
        TraceTrades("Opened: ", OpenTrades);
        TraceTrades("Closed: ", ClosedTrades);
      } catch(Exception exc) {
        IbClient.RaiseError(exc);
      }
    }

    private static Func<Trade, bool> IsEqual(PositionMessage position) => ot => ot.Key().Equals(position.Key());
    private static Func<Trade, bool> IsNotEqual(Trade trade) => ot => ot.Key().Equals(trade.Key2());

    private int CloseTrade(DateTime execTime, double execPrice, Trade closedTrade, int closeLots) {
      if(closeLots >= closedTrade.Lots) {// Full close
        closedTrade.Time2Close = execTime;
        closedTrade.Close = execPrice;

        ClosedTrades.Add(closedTrade);
        if(!OpenTrades.Remove(closedTrade))
          throw new Exception($"Couldn't remove {nameof(closedTrade)} from {nameof(OpenTrades)}");
        return closeLots - closedTrade.Lots;
      } else {// Partial close
        var trade = closedTrade.Clone();
        trade.Lots = closeLots;
        trade.Time2Close = execTime;
        trade.Close = execPrice;
        closedTrade.Lots -= trade.Lots;
        ClosedTrades.Add(trade);
        return 0;
      }
    }

    /*
 Trade ctAdd;
 if(OpenTrades.TryRemove(trade.Key2(), out ctAdd)) {
   ctAdd.Time2 = IbClient.ServerTime;
   ClosedTrades.Add(ctAdd);
 }
*/
    #region Position
    #region Position Subject
    object _PositionSubjectLocker = new object();
    ISubject<PosArg> _PositionSubject;
    ISubject<PosArg> PositionSubject {
      get {
        lock(_PositionSubjectLocker)
          if(_PositionSubject == null) {
            _PositionSubject = new Subject<PosArg>();
            _PositionSubject
              .DistinctUntilChanged(t => new { t.Item2.LocalSymbol, t.Item3.Position })
              .Take(1)
              .Subscribe(s => OnFirstPosition(s.Item2, s.Item3), exc => { IbClient.RaiseError(exc); }, () => { _defaultMessageHandler($"{nameof(PositionSubject)} is done."); });
          }
        return _PositionSubject;
      }
    }
    void OnPositionSubject(string account, Contract contract, PositionMessage pm) {
      PositionSubject.OnNext(Tuple.Create(account, contract, pm));
    }
    #endregion

    private void OnPosition(string account, Contract contract, double pos, double avgCost) {
      var position = new PositionMessage(account, contract, pos, avgCost);
      _positions.AddOrUpdate(position.Key().Item1, position, (k, p) => position);
      OnPositionSubject(account, contract, position);
      IbClient.ClientSocket.reqExecutions(IbClient.NextOrderId, new ExecutionFilter() {
        Symbol = contract.LocalSymbol,
        //Side = (trade.IsBuy ? ExecutionFilter.Sides.BUY : ExecutionFilter.Sides.SELL) + "",
        Time = IbClient.ServerTime.ToTWSString()
      });
    }

    private void OnFirstPosition(Contract contract, PositionMessage position) {
      _defaultMessageHandler(position);
      var st = IbClient.ServerTime;

      if(position.Position != 0 && !OpenTrades.Any(IsEqual(position)))
        OpenTrades.Add(TradeFromPosition(contract.LocalSymbol, position, st));

      TraceTrades("Opened: ", OpenTrades.AsEnumerable());
    }

    private void TraceTrades(string label, IEnumerable<Trade> trades) {
      _defaultMessageHandler(label + (trades.Count() > 1 ? "\n" : "")
        + string.Join("\n", trades.Select(ot => new { ot.Pair, ot.Position, ot.Time })));
    }

    private Trade TradeFromPosition(string symbol, PositionMessage position, DateTime st) {
      var trade = Trade.Create<Trade>(symbol, TradesManagerStatic.GetPointSize(symbol), CommissionByTrade);
      trade.Id = DateTime.Now.Ticks + "";
      trade.Buy = position.Position > 0;
      trade.IsBuy = trade.Buy;
      trade.Time2 = st;
      trade.Time2Close = IbClient.ServerTime;
      trade.Open = position.AverageCost;
      trade.Lots = position.Position.Abs().ToInt();
      trade.OpenOrderID = "";
      return trade;
    }
    #endregion

    private void OnUpdatePortfolio(Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealisedPNL, double realisedPNL, string accountName) {
      //_defaultMessageHandler(new UpdatePortfolioMessage(contract, position, marketPrice, marketValue, averageCost, unrealisedPNL, realisedPNL, accountName));
    }

    private void OnUpdateAccountValue(string key, string value, string currency, string accountName) {
      if(currency == _accountCurrency) {
        switch(key) {
          case "NetLiquidation":
            Account.Equity = double.Parse(value);
            break;
          case "EquityWithLoanValue":
          case "AvailableFunds":
            Account.Balance = double.Parse(value);
            break;
          case "MaintMarginReq":
            Account.UsableMargin = double.Parse(value);
            break;
        }
        //_defaultMessageHandler(new AccountValueMessage(key, value, currency, accountName));
      }
    }

    private void OnAccountSummary(int requestId, string account, string tag, string value, string currency) {
      if(currency == _accountCurrency) {
        switch(tag) {
          case "NetLiquidation":
            Account.Equity = double.Parse(value);
            break;
          case "EquityWithLoanValue":
          case "FullAvailableFunds":
            Account.Balance = double.Parse(value);
            break;
          case "MaintMarginReq":
            Account.UsableMargin = double.Parse(value);
            break;
        }
        //_defaultMessageHandler(new AccountSummaryMessage(requestId, account, tag, value, currency));
      }
    }
    private void OnAccountSummaryEnd(int obj) {
      accountSummaryRequestActive = false;
    }
    #endregion

    #region Requests
    public void RequestAccountSummary() {
      if(!accountSummaryRequestActive) {
        accountSummaryRequestActive = true;
        IbClient.ClientSocket.reqAccountSummary(NextReqId(), "All", ACCOUNT_SUMMARY_TAGS);
      } else {
        IbClient.ClientSocket.cancelAccountSummary(CurrReqId());
      }
    }

    public void SubscribeAccountUpdates() {
      if(!accountUpdateRequestActive) {
        accountUpdateRequestActive = true;
        IbClient.ClientSocket.reqAccountUpdates(true, _accountId);
      } else {
        IbClient.ClientSocket.reqAccountUpdates(false, _accountId);
        accountUpdateRequestActive = false;
      }
    }

    public void RequestPositions() {
      IbClient.ClientSocket.reqPositions();
    }
    #endregion

    #region Overrrides
    public override string ToString() {
      return new { IbClient, CurrentAccount = _accountId } + "";
    }
    #endregion
  }
  static class Mixins {
    private static Tuple<string, bool> Key(string symbol, bool isBuy) => Tuple.Create(symbol.WrapPair(), isBuy);
    private static Tuple<string, bool> Key2(string symbol, bool isBuy) => Key(symbol, !isBuy);

    public static Tuple<string, bool> Key(this PositionMessage t) => Key(t.Contract.LocalSymbol, t.IsBuy);
    public static Tuple<string, bool> Key2(this PositionMessage t) => Key2(t.Contract.LocalSymbol, t.IsBuy);

    public static Tuple<string, bool> Key(this Trade t) => Key(t.Pair, t.IsBuy);
    public static Tuple<string, bool> Key2(this Trade t) => Key2(t.Pair, t.IsBuy);
  }
}
