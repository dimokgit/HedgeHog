using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel;
using Gala = GalaSoft.MvvmLight.Command;
using FXW = Order2GoAddIn.FXCoreWrapper;
using System.Windows.Data;
using System.Data.Objects;
using System.Windows.Input;
using System.Windows;
using HedgeHog.Shared;

namespace HedgeHog.Alice.Client {
  public class RemoteControlModel : HedgeHog.Models.ModelBase {
    #region Properties
    public bool IsInDesigh { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
    FXW fw;
    public bool IsLoggedIn { get { return MasterModel.CoreFX.IsLoggedIn; } }
    public IMainModel MasterModel { get; set; }
    public ObservableCollection<string> Instruments { get; set; }
    public double[] TradingRatios { get { return new double[] { 0.1,0.5, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }; } }
    public double[] StopsAndLimits { get { return new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 20, 25, 30, 40, 50, 60, 70, 80, 90, 100, 120, 135, 150 }; } }
    public IQueryable<Models.TradingMacro> TradingMacros {
      get {
        try {
          return !IsInDesigh && MasterModel.CoreFX.IsLoggedIn ? GlobalStorage.Context.TradingMacroes : GlobalStorage.Context.TradingMacroes.Take(0);
        } catch (Exception exc) {
          Debug.Fail("TradingMacros is null.");
          return null;
        }
      }
    }
    #endregion

    #region Commands

    ICommand _DeleteTradingMacroCommand;
    public ICommand DeleteTradingMacroCommand {
      get {
        if (_DeleteTradingMacroCommand == null) {
          _DeleteTradingMacroCommand = new Gala.RelayCommand<object>(DeleteTradingMacro, (tm) => tm is Models.TradingMacro);
        }

        return _DeleteTradingMacroCommand;
      }
    }
    void DeleteTradingMacro(object tradingMacro) {
      var tm = tradingMacro as Models.TradingMacro;
      if (tm == null || tm.EntityState == System.Data.EntityState.Detached) return;
      GlobalStorage.Context.TradingMacroes.DeleteObject(tm);
      GlobalStorage.Context.SaveChanges();
    }


    ICommand _ClosePairCommand;
    public ICommand ClosePairCommand {
      get {
        if (_ClosePairCommand == null) {
          _ClosePairCommand = new Gala.RelayCommand<object>(ClosePair, (tm) => true);
        }

        return _ClosePairCommand;
      }
    }
    void ClosePair(object tradingMacro) {
      try {
        var pair = (tradingMacro as Models.TradingMacro).Pair;
        fw.FixOrdersClose(fw.GetTrades(pair).Select(t => t.Id).ToArray());
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      }
    }


    ICommand _BuyCommand;
    public ICommand BuyCommand {
      get {
        if (_BuyCommand == null) {
          _BuyCommand = new Gala.RelayCommand<object>(Buy, (tm) => true);
        }

        return _BuyCommand;
      }
    }
    void Buy(object tradingMacro) {
      var tm = tradingMacro as Models.TradingMacro;
      OpenTrade(tm, true);
      try {
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      }
    }


    ICommand _SellCommand;
    public ICommand SellCommand {
      get {
        if (_SellCommand == null) {
          _SellCommand = new Gala.RelayCommand<object>(Sell, (tm) => true);
        }

        return _SellCommand;
      }
    }
    void Sell(object tradingMacro) {
      var tm = tradingMacro as Models.TradingMacro;
      OpenTrade(tm, false);
      try {
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      }
    }


    private void OpenTrade(Models.TradingMacro tm,bool buy) {
      var tradeAmount = tm.LotSize * tm.Lots;
      var price = fw.GetPrice(tm.Pair);
      var limit = tm.Limit == 0 ? 0 : buy ? price.Ask + fw.InPoints(tm.Pair, tm.Limit) : price.Bid - fw.InPoints(tm.Pair,tm.Limit);
      var stop = tm.Stop == 0 ? 0 : buy ? price.Bid - fw.InPoints(tm.Pair, tm.Stop) : price.Ask + fw.InPoints(tm.Pair, tm.Stop);
      fw.FixOrderOpen(tm.Pair, buy, tradeAmount, limit, stop, "");
    }

    #endregion

    #region Ctor
    public RemoteControlModel(IMainModel tradesModel) {
      Instruments = new ObservableCollection<string>(new[] { "EUR/USD", "USD/JPY" });
      this.MasterModel = tradesModel;
      if (!IsInDesigh) {
        fw = new FXW(MasterModel.CoreFX);
        MasterModel.CoreFX.LoggedInEvent += CoreFX_LoggedInEvent;
        MasterModel.CoreFX.LoggedOffEvent += CoreFX_LoggedOffEvent;
        GlobalStorage.Context.ObjectMaterialized += new ObjectMaterializedEventHandler(Context_ObjectMaterialized);
      }
    }

    ~RemoteControlModel() {
      MasterModel.CoreFX.LoggedInEvent -= CoreFX_LoggedInEvent;
      MasterModel.CoreFX.LoggedOffEvent -= CoreFX_LoggedOffEvent;
    }
    #endregion

    #region Event Handlers
    void Context_ObjectMaterialized(object sender, ObjectMaterializedEventArgs e) {
      var tm = e.Entity as Models.TradingMacro;
      if (tm == null) return;
      SetLotSize(tm);
      tm.PropertyChanged += TradingMacro_PropertyChanged;
    }
    void TradingMacro_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
      var tm = sender as Models.TradingMacro;
      var propsToHandle = Lib.GetLambdas(() => tm.Pair, () => tm.TradingRatio);
      if (  propsToHandle.Contains(e.PropertyName)) SetLotSize(tm);
    }
    void CoreFX_LoggedInEvent(object sender, EventArgs e) {
      InitInstruments();
      fw.PriceChanged += fw_PriceChanged;
    }
    void CoreFX_LoggedOffEvent(object sender, EventArgs e) {
      fw.PriceChanged -= fw_PriceChanged;
    }
    void fw_PriceChanged(Bars.Price Price) {
      var summaries = fw.GetSummaries();
      foreach (var tm in TradingMacros) {
        var summary = summaries.SingleOrDefault(s => s.Pair == tm.Pair);
        tm.Net = summary != null ? summary.NetPL : (double?)null;
        tm.StopAmount = summary != null ? summary.StopAmount : (double?)null;
        tm.LimitAmount = summary != null ? summary.LimitAmount : (double?)null;
      }
    }
    #endregion

    #region Helpers
    void SetLotSize(Models.TradingMacro tm) {
      if( IsLoggedIn )
        tm.LotSize = FXW.GetLotstoTrade(fw.GetAccount().Balance, fw.Leverage(tm.Pair), tm.TradingRatio, fw.MinimumQuantity);
    }
    private void InitInstruments() {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(new Action(() => {
        while (Instruments.Count > 0) Instruments.RemoveAt(0);
        fw.GetOffers().Select(o => o.Pair).ToList().ForEach(i => Instruments.Add(i));
        RaisePropertyChanged(() => TradingMacros);
      }));
    }
    #endregion

  }
}
