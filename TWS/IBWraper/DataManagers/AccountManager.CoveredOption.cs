using HedgeHog;
using IBApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IBApp {
  public partial class AccountManager {
    public void OpenCoveredOption(Contract contract, int quantity, double price, [CallerMemberName] string Caller = "") {
      double? callStrikeMax = price == 0 ? (double?)null : price;
      CurrentOptions(contract.Instrument, price, 0, 3, c => c.IsCall)
        .SelectMany(os => os.OrderBy(o => o.option.Strike))
        .Where(o => o.option.Strike <= callStrikeMax.GetValueOrDefault(o.underPrice))
        .Take(1)
        .Subscribe(call => {
          OpenCoveredOption(contract, "", quantity, price, call.option, DateTime.MaxValue, Caller);
        });
    }
    public void OpenCoveredOption(string instrument, string option, int quantity, double price, [CallerMemberName] string Caller = "") {
      (from i in IbClient.ReqContractDetailsCached(instrument).Select(cd => cd.Contract)
       from o in IbClient.ReqContractDetailsCached(option).Select(cd => cd.Contract)
       select (i, o)
       ).Subscribe(t => OpenCoveredOption(t.i, "", quantity, price, t.o, DateTime.MaxValue, Caller));
    }
    public void OpenCoveredOption(Contract contract, string type, int quantity, double price, Contract contractOCO, DateTime goodTillDate, [CallerMemberName] string Caller = "") {
      bool FindOrer(OrderContractHolder oc, Contract c) => !oc.isDone && oc.contract == c && oc.order.TotalPosition().Sign() == quantity.Sign();
      UseOrderContracts(orderContracts => {
        var aos = orderContracts.Values.Where(oc => FindOrer(oc, contract))
        .Select(ao => new { ao.order, contract, ao.status.status }).ToArray();
        var ocos = (from ao in aos
                    join oc in orderContracts.Values on ao.order.OrderId equals oc.order.ParentId
                    select new { oc.order, contract = contractOCO, oc.status.status }).ToArray();
        if(aos.Any()) {
          aos.Concat(ocos).ForEach(ao => {
            Trace($"OpenTrade: {ao.contract} already has active order {ao.order.OrderId} with status: {ao.status}.\nUpdating {new { price, ao.contract }}");
            UpdateOrder(ao.order.OrderId, OrderPrice(price, ao.contract));
          });
        } else {
          var orderType = price == 0 ? "MKT" : type.IfEmpty("LMT");
          bool isPreRTH = orderType == "LMT";
          var order = OrderFactory(contract, quantity, price, goodTillDate, DateTime.MinValue, orderType, isPreRTH);
          var tpOrder = MakeOCOOrder(order);
          new[] { (order, contract, price), (tpOrder, contractOCO, 0) }
            .ForEach(o => {
              _verbous(new { plaseOrder = o });
              PlaceOrder( o.order, o.contract).Subscribe();
            });
        }
      }, Caller);
    }

    Order MakeOCOOrder(IBApi.Order parent) {
      parent.Transmit = false;
      return new IBApi.Order() {
        Account = parent.Account,
        ParentId = parent.OrderId,
        OrderId = NetOrderId(),
        Action = parent.Action == "BUY" ? "SELL" : "BUY",
        OrderType = "MKT",
        TotalQuantity = parent.TotalQuantity,
        Tif = parent.Tif,
        OutsideRth = parent.OutsideRth,
        OverridePercentageConstraints = true,
        Transmit = true
      };
    }

    bool OpenTradeError(Contract c, IBApi.Order o, (int id, int errorCode, string errorMsg, Exception exc) t, object context) {
      var trace = $"{nameof(OpenTradeError)}:{c}:" + (context == null ? "" : context + ":");
      var isWarning = Regex.IsMatch(t.errorMsg, @"\sWarning:") || t.errorCode == 103;
      if(!isWarning) OnOpenError(t, trace);
      else
        Trace(trace + t + "\n" + o);
      return !isWarning;
    }
  }
}
