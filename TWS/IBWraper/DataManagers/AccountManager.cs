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
    public ConcurrentDictionary<Tuple<string, bool>, Trade> OpenTrades { get; private set; } = new ConcurrentDictionary<Tuple<string, bool>, Trade>();
    public ConcurrentDictionary<Tuple<string, bool>, Trade> ClosedTrades { get; private set; } = new ConcurrentDictionary<Tuple<string, bool>, Trade>();
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
      public TradeEventArgs(Trade trade): base() {
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
    public Trade[] GetTrades() { return OpenTrades.Values.ToArray(); }
    void CloseTrade(Trade trade) {
      ClosedTrades.TryAdd(trade.Key(), trade);
      RaiseTradeRemoved(trade);
    }
    void OpenTrade(Trade trade) {
      OpenTrades.TryAdd(trade.Key(), trade);
      RaiseTradeAdded(trade);
    }
    #endregion

    #region IB Handlers
    private void OnExecDetails(int reqId, Contract contract, Execution execution) {
      var symbol = contract.LocalSymbol;
      var trade = Trade.Create(symbol, TradesManagerStatic.GetPointSize(symbol), CommissionByTrade);

      trade.Id = execution.PermId + "";
      trade.Buy = execution.Side == "BOT";
      trade.IsBuy = trade.Buy;
      trade.Time2 = execution.Time.FromTWSString();
      trade.Time2Close = execution.Time.FromTWSString();
      trade.Open = trade.Close = execution.AvgPrice;
      trade.Lots = execution.CumQty;
      trade.OpenOrderID = execution.OrderId + "";

      _defaultMessageHandler(new { contract = contract + "", execution }.ToJson());
    }
    bool _firstTime=true;
    private void OnPosition(string account, Contract contract, double pos, double avgCost) {
      var position = new PositionMessage(account, contract, pos, avgCost);
      if(!_firstTime) {
        _firstTime = false;
        var symbol = contract.LocalSymbol;
        var trade = Trade.Create(symbol, TradesManagerStatic.GetPointSize(symbol), CommissionByTrade);

        trade.Id = DateTime.Now.Ticks + "";
        trade.Buy = pos > 0;
        trade.IsBuy = trade.Buy;
        trade.Time2 = IbClient.ServerTime;
        trade.Time2Close = IbClient.ServerTime;
        trade.Open = avgCost;
        trade.Lots = pos.Abs().ToInt();
        trade.OpenOrderID = "";

        Trade otAdd;
        if(!OpenTrades.TryGetValue(trade.Key(), out otAdd))
          OpenTrades.TryAdd(trade.Key(),trade);

      }
      _defaultMessageHandler(position);
      IbClient.ClientSocket.reqExecutions(IbClient.NextOrderId, new ExecutionFilter() { Symbol = contract.LocalSymbol });
    }

    private void OnUpdatePortfolio(Contract arg1, double arg2, double arg3, double arg4, double arg5, double arg6, double arg7, string arg8) {
      _defaultMessageHandler(new UpdatePortfolioMessage(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8));
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
    public static Tuple<string, bool> Key(this Trade t) => Tuple.Create(t.Pair, t.IsBuy);
    public static Tuple<string, bool> Key2(this Trade t) => Tuple.Create(t.Pair, !t.IsBuy);
  }
}
