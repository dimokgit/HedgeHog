/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IBSampleApp.util;
using static HedgeHog.Core.JsonExtensions;

namespace IBApp {
  public class AccountManager {
    private const int ACCOUNT_ID_BASE = 50000000;

    private const int ACCOUNT_SUMMARY_ID = ACCOUNT_ID_BASE + 1;

    private const string ACCOUNT_SUMMARY_TAGS = "AccountType,NetLiquidation,TotalCashValue,SettledCash,AccruedCash,BuyingPower,EquityWithLoanValue,PreviousEquityWithLoanValue,"
             +"GrossPositionValue,ReqTEquity,ReqTMargin,SMA,InitMarginReq,MaintMarginReq,AvailableFunds,ExcessLiquidity,Cushion,FullInitMarginReq,FullMaintMarginReq,FullAvailableFunds,"
             +"FullExcessLiquidity,LookAheadNextChange,LookAheadInitMarginReq ,LookAheadMaintMarginReq,LookAheadAvailableFunds,LookAheadExcessLiquidity,HighestSeverity,DayTradesRemaining,Leverage";

    private List<string> managedAccounts;

    private bool accountSummaryRequestActive = false;
    private bool accountUpdateRequestActive = false;
    private string currentAccountSubscribedToTupdate;
    private readonly Action<object> _defaultMessageHandler;

    public override string ToString() {
      return new { IbClient, CurrentAccount = currentAccountSubscribedToTupdate } + "";
    }
    public AccountManager(IBClientCore ibClient, string accountId, Action<object> onMessage) {
      IbClient = ibClient;
      currentAccountSubscribedToTupdate = accountId;
      _defaultMessageHandler = onMessage ?? new Action<object>(o => { throw new NotImplementedException(new { onMessage } + ""); });
      IbClient.AccountSummary += OnAccountSummary;
      IbClient.AccountSummaryEnd += OnAccountSummaryEnd;
      IbClient.UpdateAccountValue += OnUpdateAccountValue;
      IbClient.UpdatePortfolio += OnUpdatePortfolio;
      ibClient.Position += OnPosition;
    }

    private void OnPosition(string arg1, IBApi.Contract arg2, double arg3, double arg4) {
      _defaultMessageHandler(new PositionMessage(arg1, arg2, arg3, arg4));
    }

    private void OnUpdatePortfolio(IBApi.Contract arg1, double arg2, double arg3, double arg4, double arg5, double arg6, double arg7, string arg8) {
      _defaultMessageHandler(new UpdatePortfolioMessage(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8));
    }

    private void OnUpdateAccountValue(string arg1, string arg2, string arg3, string arg4) {
      _defaultMessageHandler(new AccountValueMessage(arg1, arg2, arg3, arg4));
    }

    private void OnAccountSummary(int arg1, string arg2, string arg3, string arg4, string arg5) {
      _defaultMessageHandler(new AccountSummaryMessage(arg1, arg2, arg3, arg4, arg5));
    }
    private void OnAccountSummaryEnd(int obj) {
      accountSummaryRequestActive = false;
    }

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
        IbClient.ClientSocket.reqAccountUpdates(true, currentAccountSubscribedToTupdate);
      } else {
        IbClient.ClientSocket.reqAccountUpdates(false, currentAccountSubscribedToTupdate);
        accountUpdateRequestActive = false;
      }
    }

    public void RequestPositions() {
      IbClient.ClientSocket.reqPositions();
    }

    public IBClientCore IbClient { get; private set; }
  }
}
