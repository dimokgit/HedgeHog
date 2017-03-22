/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IBSampleApp.util;
using static HedgeHog.Core.JsonExtensions;
using HedgeHog.Shared;

namespace IBApp {
  public class AccountManager {
    #region Constants
    private const int ACCOUNT_ID_BASE = 50000000;

    private const int ACCOUNT_SUMMARY_ID = ACCOUNT_ID_BASE + 1;

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
    public IBClientCore IbClient { get; private set; }
    public Account Account { get;private set; }
    #endregion

    #region Methods

    #endregion

    #region Ctor
    public AccountManager(IBClientCore ibClient, string accountId, Action<object> onMessage) {
      Account = new Account();
      IbClient = ibClient;
      _accountId = accountId;
      _defaultMessageHandler = onMessage ?? new Action<object>(o => { throw new NotImplementedException(new { onMessage } + ""); });
      IbClient.AccountSummary += OnAccountSummary;
      IbClient.AccountSummaryEnd += OnAccountSummaryEnd;
      IbClient.UpdateAccountValue += OnUpdateAccountValue;
      IbClient.UpdatePortfolio += OnUpdatePortfolio;
      ibClient.Position += OnPosition;
    }
    #endregion

    #region Updates
    private void OnPosition(string arg1, IBApi.Contract arg2, double arg3, double arg4) {
      _defaultMessageHandler(new PositionMessage(arg1, arg2, arg3, arg4));
    }

    private void OnUpdatePortfolio(IBApi.Contract arg1, double arg2, double arg3, double arg4, double arg5, double arg6, double arg7, string arg8) {
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
        _defaultMessageHandler(new AccountValueMessage(key, value, currency, accountName));
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
        _defaultMessageHandler(new AccountSummaryMessage(requestId, account, tag, value, currency));
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
        IbClient.ClientSocket.reqAccountSummary(ACCOUNT_SUMMARY_ID, "All", ACCOUNT_SUMMARY_TAGS);
      } else {
        IbClient.ClientSocket.cancelAccountSummary(ACCOUNT_SUMMARY_ID);
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

    public override string ToString() {
      return new { IbClient, CurrentAccount = _accountId } + "";
    }
  }
}
