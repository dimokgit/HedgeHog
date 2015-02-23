using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq.Expressions;
using HedgeHog.Shared;
using HedgeHog.Alice.Store;

namespace HedgeHog.Alice.Store {
  public class TradingAccountModel : Shared.Account, INotifyPropertyChanged {
    public TradingAccountModel(Func<Trade,double> commissionByTrade):base() {
    }
    TradingStatistics _tradingStatistics;
    public TradingStatistics TradingStatistics {
      get { return _tradingStatistics; }
      set {
        if (_tradingStatistics == value) return;
        _tradingStatistics = value;
        RaisePropertyChanged(() => TradingStatistics);
      }
    }
    Exception Log {
      set {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(value);
      }
    }
    event EventHandler CloseAllTradesEvent;
    public event EventHandler CloseAllTrades {
      add {
        if (CloseAllTradesEvent == null || !CloseAllTradesEvent.GetInvocationList().Contains(value))
          CloseAllTradesEvent += value;
      }
      remove {
        CloseAllTradesEvent -= value;
      }
    }
    void RaiseCloseAllTrades() {
      if (CloseAllTradesEvent != null) 
        CloseAllTradesEvent(this,EventArgs.Empty);
    }

    private double _TakeProfit = double.NaN;
    public double TakeProfit {
      get {
        if (!PipsToExit.HasValue) return double.NaN;
        return PipsToExit == 0 ? _TakeProfit : PipsToExit.Value;
      }
      set {
        if (_TakeProfit != value) {
          _TakeProfit = value;
          OnPropertyChanged(() => TakeProfit);
        }
      }
    }

    #region GrossToExitInPips
    private double? _GrossToExitInPips;
    public double? GrossToExitInPips {
      get { return _GrossToExitInPips; }
      set {
        if (_GrossToExitInPips != value) {
          _GrossToExitInPips = value;
          RaisePropertyChanged("GrossToExitInPips");
        }
      }
    }

    #endregion
    private double? _PipsToExit;
    public double? PipsToExit {
      get { return _PipsToExit; }
      set {
        if (_PipsToExit != value) {
          _PipsToExit = value;
          RaisePropertyChanged(() => TakeProfit);
          RaisePropertyChanged(() => PipsToExit);
        }
      }
    }

    private double? _DayTakeProfit;
    public double? DayTakeProfit {
      get { return _DayTakeProfit; }
      set {
        if (_DayTakeProfit != value) {
          _DayTakeProfit = value;
          OnPropertyChanged(() => DayTakeProfit);
        }
      }
    }


    public double TradingRatio { get; set; }
    public double ProfitPercent { get { return Equity / Balance - 1; } }
    private double _CurrentLoss;
    public double CurrentLoss {
      get { return _CurrentLoss; }
      set {
        if (_CurrentLoss != value) {
          _CurrentLoss = value;
          OnPropertyChanged(() => CurrentLoss, () => OriginalBalance, () => OriginalProfit);
        }
      }
    }

    #region CurrentGrossInPips
    private double _CurrentGrossInPips;
    public double CurrentGrossInPips {
      get { return _CurrentGrossInPips; }
      set {
        if (_CurrentGrossInPips != value) {
          _CurrentGrossInPips = value;
          RaisePropertyChanged("CurrentGrossInPips");
        }
      }
    }

    #endregion

    public double OriginalBalance { get { return Balance - CurrentLoss; } }
    public double OriginalProfit { get { return Equity / OriginalBalance - 1; } }

    public void Update(Account account,double tradingRatio,TradingStatistics tradingStatistics,DateTime serverTime) {
      try {
        TradingAccountModel accountRow = this;
        accountRow.Balance = account.Balance;
        accountRow.Equity = account.Equity;
        accountRow.Hedging = account.Hedging;
        accountRow.DayPL = account.DayPL;
        accountRow.ID = account.ID;
        accountRow.IsMarginCall = account.IsMarginCall;
        accountRow.PipsToMC = account.PipsToMC;
        accountRow.UsableMargin = account.UsableMargin;
        accountRow.Trades = account.Trades;
        accountRow.TradingRatio = tradingRatio;
        accountRow.ServerTime = serverTime;
        accountRow.StopAmount = account.StopAmount;
        accountRow.LimitAmount = account.LimitAmount;
        accountRow.TradingStatistics = tradingStatistics;
        accountRow.TakeProfit = tradingStatistics.TakeProfitDistanceInPips;
        accountRow.CurrentGrossInPips = tradingStatistics.CurrentGrossInPips;
        if (accountRow.CurrentGrossInPips >= GrossToExitInPips) {
          RaiseCloseAllTrades();
          GrossToExitInPips = null;
        }
        if (accountRow.PL >= accountRow.TakeProfit) {
          RaiseCloseAllTrades();
          PipsToExit = null;
        }
        if (DayTakeProfit.HasValue && account.DayPL >= DayTakeProfit) {
          RaiseCloseAllTrades();
          DayTakeProfit = DayTakeProfit > 0 ? DayTakeProfit * 2 : null;
        }
        accountRow.OnPropertyChanged(
        () => accountRow.Balance,
        () => accountRow.Equity,
        () => accountRow.DayPL,
        () => accountRow.Hedging,
        () => accountRow.ID,
        () => accountRow.IsMarginCall,
        () => accountRow.PipsToMC,
        () => accountRow.PL,
        () => accountRow.Net,
        () => accountRow.UsableMargin,
        () => accountRow.Trades,
        () => accountRow.HasProfit,
        () => accountRow.TradingRatio,
        () => accountRow.StopAmount,
        () => accountRow.BalanceOnStop,
        () => accountRow.LimitAmount,
        () => accountRow.BalanceOnLimit,
        () => accountRow.StopToBalanceRatio,
        () => accountRow.ProfitPercent,
        () => accountRow.ServerTime,
        () => accountRow.OriginalBalance,
        () => accountRow.OriginalProfit
          );
        //if (OriginalProfit >= .001) RaiseCloseAllTrades();
      } catch (Exception exc) {
        Log = exc;
      }
    }

    public bool HasProfit { get { return Net > 0; } }
    public void OnPropertyChanged(params Expression<Func<object>>[] propertyLamdas) {
      foreach (var pl in propertyLamdas) 
        RaisePropertyChanged(pl);
    }
    protected void RaisePropertyChanged(Expression<Func<object>> propertyLamda) {
      var bodyUE = propertyLamda.Body as UnaryExpression;
      if (bodyUE == null) {
        var s = (propertyLamda.Body as Expression).ToString().Split('.');
        RaisePropertyChanged(s.Last().Split(')')[0]);
      } else {
        var operand = bodyUE.Operand as MemberExpression;
        var member = operand.Member;
        RaisePropertyChanged(member.Name);
      }
    }

    protected virtual void RaisePropertyChanged(params LambdaExpression[] propertyExpressions) {
      if (PropertyChanged != null)
        foreach (var propertyExpression in propertyExpressions)
          RaisePropertyChanged(propertyExpression.GetLambda());
      }
    protected virtual void RaisePropertyChanged(string propertyName) {
      VerifyPropertyName(propertyName);

      var handler = PropertyChanged;

      if (handler != null) {
        handler(this, new PropertyChangedEventArgs(propertyName));
      }
    }

    /// <summary>
    /// Verifies that a property name exists in this ViewModel. This method
    /// can be called before the property is used, for instance before
    /// calling RaisePropertyChanged. It avoids errors when a property name
    /// is changed but some places are missed.
    /// <para>This method is only active in DEBUG mode.</para>
    /// </summary>
    /// <param name="propertyName"></param>
    [Conditional("DEBUG")]
    [DebuggerStepThrough]
    public void VerifyPropertyName(string propertyName) {
      var myType = this.GetType();
      if (myType.GetProperty(propertyName) == null) {
        throw new ArgumentException("Property not found", propertyName);
      }
    }

    #region INotifyPropertyChanged Members

    public event PropertyChangedEventHandler PropertyChanged;

    #endregion


  }
}
