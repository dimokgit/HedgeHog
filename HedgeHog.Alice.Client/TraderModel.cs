using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using O2G = Order2GoAddIn;
using System.ServiceModel;
using System.Windows.Data;
namespace HedgeHog.Alice.Client {
  class TraderModel:HedgeHog.Models.ModelBase {
    ThreadScheduler GetTradesScheduler;
    ObservableCollection<O2G.Trade> _serverTrades = new ObservableCollectionEx<O2G.Trade>();
    public ObservableCollection<O2G.Trade> ServerTrades {
      get { return _serverTrades; }
      set { _serverTrades = value; RaisePropertyChangedCore(); }
    }
    public ListCollectionView ServerTradesList { get; set; }
    string _wcfPath = "net.tcp://localhost:9200//HedgeHog.Alice.WCF";
    public string WcfPath {
      get { return _wcfPath; }
      set { _wcfPath = value; RaisePropertyChangedCore(); }
    }
    public TraderModel() {
      ServerTradesList = new ListCollectionView(ServerTrades);
      GetTradesScheduler = new ThreadScheduler(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 
        () => {
          Using(tsc => {
            var trades = tsc.GetTrades().ToList();
            ServerTradesList.Dispatcher.BeginInvoke(new Action(() => {
              ServerTrades.Clear();
              trades.ForEach(t => ServerTrades.Add(t));
            }));
          });
        },
        (s, e) => { });
    }

    public void Using(Action<TraderService.TraderServiceClient> action) {
      var service = new TraderService.TraderServiceClient();//"NetTcpBinding_ITraderService", WcfPath);
      bool success = false;
      try {
        action(service);
        if (service.State != CommunicationState.Faulted) {
          service.Close();
          success = true;
        }
      } finally {
        if (!success) {
          service.Abort();
        }
      }
    }
  }
}
