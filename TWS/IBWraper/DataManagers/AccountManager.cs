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
using System.Diagnostics;

namespace IBApp {
  public class AccountManager : DataManager {
    #region Constants
    private const int ACCOUNT_ID_BASE = 50000000;

    private const string ACCOUNT_SUMMARY_TAGS = "AccountType,NetLiquidation,TotalCashValue,SettledCash,AccruedCash,BuyingPower,EquityWithLoanValue,PreviousEquityWithLoanValue,"
             +"GrossPositionValue,ReqTEquity,ReqTMargin,SMA,InitMarginReq,MaintMarginReq,AvailableFunds,ExcessLiquidity,Cushion,FullInitMarginReq,FullMaintMarginReq,FullAvailableFunds,"
             +"FullExcessLiquidity,LookAheadNextChange,LookAheadInitMarginReq ,LookAheadMaintMarginReq,LookAheadAvailableFunds,LookAheadExcessLiquidity,HighestSeverity,DayTradesRemaining,Leverage";
    //private const int BaseUnitSize = 1;
    #endregion

    #region Fields
    private bool accountSummaryRequestActive = false;
    private bool accountUpdateRequestActive = false;
    private string _accountId;
    private readonly Action<object> _defaultMessageHandler;
    private Action<object> _verbous => _defaultMessageHandler;
    private readonly string _accountCurrency="USD";
    #endregion

    #region Properties
    public Account Account { get; private set; }
    private readonly ReactiveList<Trade> OpenTrades = new ReactiveList<Trade>();
    private readonly ConcurrentDictionary<string, PositionMessage> _positions = new ConcurrentDictionary<string, PositionMessage>();
    private readonly ReactiveList<Trade> ClosedTrades = new ReactiveList<Trade>();
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
      IbClient.ExecDetails += OnExecution;
      IbClient.Position += OnPosition;
      //ibClient.UpdatePortfolio += IbClient_UpdatePortfolio;
      OpenTrades.ItemsAdded.Subscribe(RaiseTradeAdded);
      OpenTrades.ItemsRemoved.Subscribe(RaiseTradeRemoved);
      ClosedTrades.ItemsAdded.Subscribe(RaiseTradeClosed);
      _defaultMessageHandler(nameof(AccountManager) + " is ready");
    }

    private void IbClient_UpdatePortfolio(Contract arg1, double arg2, double arg3, double arg4, double arg5, double arg6, double arg7, string arg8) {

    }

    Func<Trade, double> IBCommissionByTrade(Trade trade) {
      var commissionPerUnit = CommissionByTrade(trade) / trade.Lots;
      return t => t.Lots * commissionPerUnit;
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
    //public class TradeEventArgs : EventArgs {
    //  public Trade Trade { get; private set; }
    //  public TradeEventArgs(Trade trade) : base() {
    //    Trade = trade;
    //  }
    //}
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
      TradeAddedEvent?.Invoke(this, new TradeEventArgs(trade));
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
      TradeRemovedEvent?.Invoke(this, new TradeEventArgs(trade));
    }
    #endregion

    #region TradeClosedEvent
    event EventHandler<TradeEventArgs> TradeClosedEvent;
    public event EventHandler<TradeEventArgs> TradeClosed {
      add {
        if (TradeClosedEvent == null || !TradeClosedEvent.GetInvocationList().Contains(value))
          TradeClosedEvent += value;
      }
      remove {
        if (TradeClosedEvent != null)
          TradeClosedEvent -= value;
      }
    }
    void RaiseTradeClosed(Trade trade) {
      trade.Kind = PositionBase.PositionKind.Closed;
      trade.TradesManager = null;
      TradeClosedEvent?.Invoke(this, new TradeEventArgs(trade));
    }
    #endregion


    #endregion

    #region Trades
    public Trade[] GetTrades() { return OpenTrades.ToArray(); }
    public Trade[] GetClosedTrades() { return ClosedTrades.ToArray(); }
    #endregion
    public class PositionNotFoundException : Exception {
      public PositionNotFoundException(string symbol) : base(new { symbol } + "") { }
    }
    #region IB Handlers
    #region Execution Subject
    object _ExecutionSubjectLocker = new object();
    ISubject<Tuple<int,Contract,Execution>> _ExecutionSubject;
    ISubject<Tuple<int, Contract, Execution>> ExecutionSubject {
      get {
        lock(_ExecutionSubjectLocker)
          if(_ExecutionSubject == null) {
            _ExecutionSubject = new Subject<Tuple<int, Contract, Execution>>();
            _ExecutionSubject
              //.Where(t => t.Item1 == -1)
              //.DistinctUntilChanged(t => {
              //  //_defaultMessageHandler(new { ExecutionSubject = new { OrderId = t.Item3?.OrderId } });
              //  return t.Item3?.ExecId;
              //})
              .Subscribe(s => OnExecDetails(s.Item1, s.Item2, s.Item3), exc => _defaultMessageHandler(exc), () => _defaultMessageHandler(new { _defaultMessageHandler = "Completed" }));
          }
        return _ExecutionSubject;
      }
    }
    void OnExecution(int reqId, Contract contract, Execution execution) {
      ExecutionSubject.OnNext(Tuple.Create(reqId, contract, execution));
    }
    #endregion
    ConcurrentDictionary<string,int> _executions= new ConcurrentDictionary<string, int>();
    private void OnExecDetails(int reqId, Contract contract, Execution execution) {
      try {
        //_verbous(new { OnExecDetails = new { reqId, contract, execution } });
        var symbol = contract.Instrument;
        var execTime = execution.Time.FromTWSString();
        var execPrice = execution.AvgPrice;
        var orderId = execution.OrderId + "";

        #region Create Trade

        var trade = Trade.Create(IbClient, symbol, TradesManagerStatic.GetPointSize(symbol),TradesManagerStatic.GetBaseUnitSize(symbol), null);
        trade.Id = execution.PermId + "";
        trade.Buy = execution.Side == "BOT";
        trade.IsBuy = trade.Buy;
        trade.Time2 = execTime;
        trade.Time2Close = execution.Time.FromTWSString();
        trade.Open = trade.Close = execPrice;

        _executions.TryGetValue(orderId, out int orderLot);
        trade.Lots = execution.CumQty - orderLot;
        _executions.AddOrUpdate(orderId, execution.CumQty, (s, i) => execution.CumQty);

        trade.OpenOrderID = execution.OrderId + "";
        trade.CommissionByTrade = IBCommissionByTrade(trade);
        #endregion

        #region Close Trades
        OpenTrades
          .ToArray()
          .Where(t => t.OpenOrderID != orderId)
          .Where(IsNotEqual(trade))
          .Scan(0, (lots, otClose) => {
            var cL = CloseTrade(execTime, execPrice, otClose, trade.Lots);
            return trade.Lots = cL;
          })
          .TakeWhile(l => l > 0)
          .Count();
        #endregion

        if(trade.Lots > 0) {
          //var oldTrade = OpenTrades.SingleOrDefault(t => t.OpenOrderID == orderId);
          //if(oldTrade != null) {
          //  oldTrade.Lots += trade.Lots;
          //  oldTrade.Open = execution.AvgPrice;
          //} else
            OpenTrades.Add(trade);
        }

        OpenTrades
          .Where(t => t.Lots == 0)
          .ToList()
          .ForEach(t => OpenTrades.Remove(t));

        PositionMessage position;
        if(!_positions.TryGetValue(contract.LocalSymbol, out position))
          throw new PositionNotFoundException(symbol);
        //var tradeByPosition = TradeFromPosition(contract.LocalSymbol, position, execTime,orderId);
        //TraceTrades("tradeByPositiont:", new[] { tradeByPosition });

        //var openLots = (from ot in OpenTrades
        //                where tradeByPosition.Pair == ot.Pair
        //                group ot by ot.IsBuy into g
        //                select new { isBuy = g.Key, sum = g.Sum(t => t.Lots) }
        //                ).ToArray();
        //try {
        //  Passager.ThrowIf(() => openLots.Length > 1);
        //  openLots.ForEach(openLot => {
        //    Passager.ThrowIf(() => openLot.isBuy != position.IsBuy);
        //    Passager.ThrowIf(() => openLot.sum != position.Quantity);
        //  });
        //}catch(Exception exc) {
        //  _defaultMessageHandler(exc);
        //  OpenTrades.ToList().ForEach(ot => CloseTrade(execTime, execPrice, ot, ot.Lots));
        //  OpenTrades.Add(tradeByPosition);
        //}
        TraceTrades("Opened: ", OpenTrades);
        TraceTrades("Closed: ", ClosedTrades);
      } catch(Exception exc) {
        _defaultMessageHandler(exc);
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
        trade.CommissionByTrade = closedTrade.CommissionByTrade;
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
    #region Position Subject - Fires once
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
              .Subscribe(s => OnFirstPosition(s.Item2, s.Item3), exc => _defaultMessageHandler(exc), () => { _defaultMessageHandler($"{nameof(PositionSubject)} is done."); });
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
      //_defaultMessageHandler(nameof(OnPosition) + ": " + string.Join("\n", _positions));
      OnPositionSubject(account, contract, position);
      //IbClient.ClientSocket.reqExecutions(IbClient.NextOrderId, new ExecutionFilter() {
      //  Symbol = contract.LocalSymbol,
      //  //Side = (trade.IsBuy ? ExecutionFilter.Sides.BUY : ExecutionFilter.Sides.SELL) + "",
      //  Time = IbClient.ServerTime.ToTWSString()
      //});
    }

    private void OnFirstPosition(Contract contract, PositionMessage position) {
      _defaultMessageHandler(position);
      var st = IbClient.ServerTime;

      if(position.Position != 0 && !OpenTrades.Any(IsEqual(position)))
        OpenTrades.Add(TradeFromPosition(contract.LocalSymbol, position, st, ""));

      TraceTrades("Opened: ", OpenTrades);
    }

    private void TraceTrades(string label, IEnumerable<Trade> trades) {
      _defaultMessageHandler(label + (trades.Count() > 1 ? "\n" : "")
        + string.Join("\n", trades.Select(ot => new { ot.Pair, ot.Position, ot.Time, ot.Commission })));
    }

    private Trade TradeFromPosition(string symbol, PositionMessage position, DateTime st, string openOrderId) {
      var trade = Trade.Create(IbClient, symbol, TradesManagerStatic.GetPointSize(symbol), TradesManagerStatic.GetBaseUnitSize(symbol), null);
      trade.Id = DateTime.Now.Ticks + "";
      trade.Buy = position.Position > 0;
      trade.IsBuy = trade.Buy;
      trade.Time2 = st;
      trade.Time2Close = IbClient.ServerTime;
      trade.Open = position.AverageCost;
      trade.Lots = position.Position.Abs().ToInt();
      trade.OpenOrderID = openOrderId;
      trade.CommissionByTrade = IBCommissionByTrade(trade);
      return trade;
    }
    #endregion

    private void OnUpdatePortfolio(Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealisedPNL, double realisedPNL, string accountName) {
      var pu = new UpdatePortfolioMessage(contract, position, marketPrice, marketValue, averageCost, unrealisedPNL, realisedPNL, accountName);
    }

    private void OnUpdateAccountValue(string key, string value, string currency, string accountName) {
      if(currency == _accountCurrency) {
        switch(key) {
          case "EquityWithLoanValue":
            Account.Equity = double.Parse(value);
            break;
          case "MaintMarginReq":
            Account.UsableMargin = double.Parse(value);
            break;
          case "ExcessLiquidity":
            Account.ExcessLiquidity = double.Parse(value);
            break;
          case "UnrealizedPnL":
            Account.Balance = Account.Equity - double.Parse(value);
            break;
        }
        //_defaultMessageHandler(new AccountValueMessage(key, value, currency, accountName));
      }
    }

    private void OnAccountSummary(int requestId, string account, string tag, string value, string currency) {
      if(currency == _accountCurrency) {
        switch(tag) {
          case "EquityWithLoanValue":
            Account.Equity = double.Parse(value);
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
  public static class Mixins {
    private static Tuple<string, bool> Key(string symbol, bool isBuy) => Tuple.Create(symbol.WrapPair(), isBuy);
    private static Tuple<string, bool> Key2(string symbol, bool isBuy) => Key(symbol, !isBuy);

    public static Tuple<string, bool> Key(this PositionMessage t) => Key(t.Contract.LocalSymbol, t.IsBuy);
    public static Tuple<string, bool> Key2(this PositionMessage t) => Key2(t.Contract.LocalSymbol, t.IsBuy);

    public static Tuple<string, bool> Key(this Trade t) => Key(t.Pair, t.IsBuy);
    public static Tuple<string, bool> Key2(this Trade t) => Key2(t.Pair, t.IsBuy);
  }
}
