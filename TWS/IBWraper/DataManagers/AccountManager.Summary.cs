using System;
using System.Collections.Generic;
using System.Linq;
using HedgeHog.Shared;
using IBApi;
using HedgeHog;
using System.Collections.Concurrent;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ReactiveUI;
using PosArg = System.Tuple<string, IBApi.Contract, IBApp.PositionMessage>;
using System.Diagnostics;
using OpenOrderArg = System.Tuple<int, IBApi.Contract, IBApi.Order, IBApi.OrderState>;
using OrderStatusArg = System.Tuple<int, string, double, double, bool, double>;
using IBSampleApp.messages;

namespace IBApp {
  partial class AccountManager :DataManager {
    public double InitialMarginRequirement { get; private set; }

    private void OnUpdatePortfolio(UpdatePortfolioMessage m) {
      var pu = new UpdatePortfolioMessage(m.Contract, m.Position, m.MarketPrice, m.MarketValue, m.AverageCost, m.UnrealisedPNL, m.RealisedPNL, m.AccountName);
    }

    private void OnUpdateAccountValue(AccountValueMessage m) {
      if(m.Currency == _accountCurrency) {
        switch(m.Key) {
          //case "EquityWithLoanValue":
          case "NetLiquidation":
            Account.Equity = double.Parse(m.Value);
            break;
          case "MaintMarginReq":
            Account.UsableMargin = double.Parse(m.Value);
            break;
          case "ExcessLiquidity":
            Account.ExcessLiquidity = double.Parse(m.Value);
            break;
          case "UnrealizedPnL":
            Account.Balance = Account.Equity - double.Parse(m.Value);
            break;
          case "InitMarginReq":
            InitialMarginRequirement = double.Parse(m.Value);
            break;
        }
        //_defaultMessageHandler(new AccountValueMessage(key, value, currency, accountName));
      }
    }

    //private void OnAccountSummary(int requestId, string account, string tag, string value, string currency) {
    private void OnAccountSummary(AccountSummaryMessage msg) {
      if(msg.Currency == _accountCurrency && msg.Account == _accountId) {
        switch(msg.Tag) {
          case "NetLiquidation":
            Account.Equity = double.Parse(msg.Value);
            break;
          case "MaintMarginReq":
            Account.UsableMargin = double.Parse(msg.Value);
            break;
        }
        //_defaultMessageHandler(new AccountSummaryMessage(requestId, account, tag, value, currency));
      }
    }

    private void OnAccountSummaryEnd(AccountSummaryEndMessage msg) {
      accountSummaryRequestActive = false;
    }

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
  }
}
