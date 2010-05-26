using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel;
using FXW = Order2GoAddIn.FXCoreWrapper;
using System.Windows.Data;
using System.Data.Objects;
using System.Diagnostics;

namespace HedgeHog.Alice.Client {
  public class RemoteControlModel : HedgeHog.Models.ModelBase {
    FXW fw;
    public IMainModel MasterModel { get; set; }
    public ObservableCollection<string> Instruments { get; set; }
    public int[] TradingRatios { get { return new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }; } }

    public IQueryable<Models.TradingMacro> TradingMacros {
      get {
        try {
          return MasterModel.CoreFX.IsLoggedIn ? GlobalStorage.Context.TradingMacroes : GlobalStorage.Context.TradingMacroes.Take(0);
        } catch (Exception exc) {
          Debug.Fail("TradingMacros is null.");
          return null;
        }
      }
    }

    
    public RemoteControlModel(IMainModel tradesModel) {
      Instruments = new ObservableCollection<string>(new[] { "EUR/USD", "USD/JPY" });
      this.MasterModel = tradesModel;
      fw = new FXW(MasterModel.CoreFX);
      MasterModel.CoreFX.LoggedInEvent += CoreFX_LoggedInEvent;
    }
    ~RemoteControlModel() {
      MasterModel.CoreFX.LoggedInEvent -= CoreFX_LoggedInEvent;
    }
    void CoreFX_LoggedInEvent(object sender, EventArgs e) {
      InitInstruments();
    }

    private void InitInstruments() {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(new Action(() => {
        while (Instruments.Count > 0) Instruments.RemoveAt(0);
        fw.GetOffers().Select(o => o.Pair).ToList().ForEach(i => Instruments.Add(i));
        RaisePropertyChanged(() => TradingMacros);
      }));
    }

  }
  public class TradingMacro {
    public string Instrument { get; set; }
    public int TradingRatio { get; set; }
    public TradingMacro(string instrument,int tradingRatio) {
      this.Instrument = instrument;
      this.TradingRatio = tradingRatio;
    }
  }
}
