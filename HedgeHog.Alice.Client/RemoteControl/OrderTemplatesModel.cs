using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using HedgeHog.Alice;
using Gala = GalaSoft.MvvmLight.Command;
using System.Windows.Input;
using System.Windows;
using System.Diagnostics;
using HedgeHog.Alice.Store;
using System.Data.Entity.Core.Objects;

namespace HedgeHog.Alice.Client {
  [Export]
  class OrderTemplatesModel :HedgeHog.Models.ModelBase {

    #region Properties
    public ObjectSet<Store.OrderTemplate> OrderTemplates { get { return GlobalStorage.UseAliceContext(c=>c.OrderTemplates); } }

    TraderModel _MasterModel;
    [Import]
    public TraderModel MasterModel {
      get { return _MasterModel; }
      set {
        if (_MasterModel != value) {
          _MasterModel = value;
          RaisePropertyChangedCore();
        }
      }
    }

    public int LotSize { get { return 1000; } }
    private string[] _AvailiblePairs = new string[0];
    public string[] AvailiblePairs {
      get { return _AvailiblePairs; }
      set {
        if (_AvailiblePairs != value) {
          _AvailiblePairs = value;
          RaisePropertyChangedCore();
        }
      }
    }

    #endregion

    #region Ctor
    public OrderTemplatesModel() {
      if (App.container != null) {
        App.container.SatisfyImportsOnce(this);
        MasterModel.CoreFX.LoggedIn += CoreFX_LoggedInEvent;
      }
    }
    #endregion

    #region Event handlers
    void CoreFX_LoggedInEvent(object sender, Order2GoAddIn.LoggedInEventArgs e) {
      AvailiblePairs = MasterModel.CoreFX.Instruments;
    }
    #endregion

    #region Commands

    #region BuyOrderCommand
    ICommand _BuyOrderCommand;
    public ICommand BuyOrderCommand {
      get {
        if (_BuyOrderCommand == null) {
          _BuyOrderCommand = new Gala.RelayCommand<object>(BuyOrder, (ot) => true);
        }

        return _BuyOrderCommand;
      }
    }
    void BuyOrder(object ot) {
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(ot as Store.OrderTemplate,true);
    }
    #endregion

    #region SellOrderCommand
    ICommand _SellOrderCommand;
    public ICommand SellOrderCommand {
      get {
        if (_SellOrderCommand == null) {
          _SellOrderCommand = new Gala.RelayCommand<object>(SellOrder, (ot) => true);
        }

        return _SellOrderCommand;
      }
    }
    void SellOrder(object ot) {
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(ot as Store.OrderTemplate,false);
    }
    #endregion

    #endregion

  }
}
