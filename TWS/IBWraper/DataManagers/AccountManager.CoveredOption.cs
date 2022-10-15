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
      CurrentOptions(contract.Instrument, price, (0, DateTime.MinValue), 3, c => c.IsCall)
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
       ).Subscribe(t => OpenCoveredOption(t.i, "", quantity, price, t.o, DateTime.MaxValue, null, Caller));
    }
    public void OpenCoveredOption(Contract contract, string type, int quantity, double price, Contract contractOCO, DateTime goodTillDate, [CallerMemberName] string Caller = "") {
      OpenCoveredOption(contract, type, quantity, price, contractOCO, quantity, goodTillDate, DateTime.MinValue, null, Caller);
    }
    public void OpenCoveredOption(Contract contract, string type, int quantity, double price, Contract contractOCO, DateTime goodTillDate, OrderCondition condition = null, [CallerMemberName] string Caller = "") {
      OpenCoveredOption(contract, type, quantity, price, contractOCO, quantity, goodTillDate, DateTime.MinValue, condition, Caller);
    }
    public void OpenCoveredOption(Contract contract, string type, int quantity, double price, Contract contractOCO, int quantityOCO, DateTime goodTillDate
      , DateTime goodAfterDate, OrderCondition condition = null, [CallerMemberName] string Caller = "") {
      bool FindOrer(OrderContractHolder oc, Contract c) => !oc.isDone && oc.contract == c && oc.order.TotalPosition().Sign() == quantity.Sign();
      UseOrderContracts(orderContracts => {
        var aos = orderContracts.Where(oc => FindOrer(oc, contract))
        .Select(ao => new { ao.order, contract, ao.status.status }).ToArray();
        var ocos = (from ao in aos
                    join oc in orderContracts on ao.order.OrderId equals oc.order.ParentId
                    select new { oc.order, contract = contractOCO, oc.status.status }).ToArray();
        if(aos.Any()) {
          aos.Concat(ocos).ForEach(ao => {
            Trace($"OpenTrade: {ao.contract} already has active order {ao.order.OrderId} with status: {ao.status}.\nUpdating {new { price, ao.contract }}");
            UpdateOrder(ao.order.OrderId, Order.OrderPrice(price, ao.contract), false);
          });
        } else {
          var orderType = price == 0 ? "MKT" : type.IfEmpty("LMT");
          bool isPreRTH = orderType == "LMT";
          var order = OrderFactory(contract, quantity, price, goodTillDate, goodAfterDate, orderType, isPreRTH);
          order.Conditions.AddRange(condition.YieldNotNull());
          var tpOrder = MakeOCOOrder(order, -quantityOCO);
          new[] { (order, contract, price), (tpOrder, contractOCO, 0) }
            .ForEach(o => {
              _verbous(new { plaseOrder = o });
              PlaceOrder(o.order, o.contract).Subscribe();
            });
        }
      }, Caller);
    }

    public Order MakeOCOOrder(Order parent, int quantity) {
      parent.Transmit = false;
      return new Order() {
        Account = parent.Account,
        ParentId = parent.OrderId,
        OrderId = NetOrderId(),
        Action = quantity < 0 ? "SELL" : "BUY",
        OrderType = "MKT",
        TotalQuantity = quantity.Abs(),
        Tif = parent.Tif,
        OutsideRth = parent.OutsideRth,
        OverridePercentageConstraints = true,
        Transmit = true
      };
    }

    bool OpenTradeError(Contract c, Order o, (int id, int errorCode, string errorMsg, Exception exc) t, object context) {
      var trace = $"{nameof(OpenTradeError)}:{c}:" + (context == null ? "" : context + ":");
      var isWarning = t.errorCode == 103 || t.errorCode == 2109;
      if(!isWarning) OnOpenError(t, trace);
      //else Trace(trace + t + "\n" + o);
      return !isWarning;
    }
  }
}
