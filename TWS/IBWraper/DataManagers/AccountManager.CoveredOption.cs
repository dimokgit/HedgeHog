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
    public void OpenCoveredOption(Contract contract, int quantity, double price, int minTickMultiplier = 1, [CallerMemberName] string Caller = "") {
      double? callStrikeMax = price == 0 ? (double?)null : price;
      CurrentOptions(contract.Instrument, price, 0, 3, c => c.IsCall)
        .SelectMany(os => os.OrderBy(o => o.option.Strike))
        .Where(o => o.option.Strike <= callStrikeMax.GetValueOrDefault(o.underPrice))
        .Take(1)
        .Subscribe(call => {
          OpenCoveredOption(contract, "", quantity, price, call.option, DateTime.MaxValue, minTickMultiplier, Caller);
        });
    }
    public void OpenCoveredOption(Contract contract, string type, int quantity, double price, Contract contractOCO, DateTime goodTillDate, int minTickMultiplier = 1, [CallerMemberName] string Caller = "") {
      UseOrderContracts(orderContracts => {
        var aos = orderContracts
          .Where(oc => !oc.isDone && oc.contract.Key == contract.Key && oc.order.TotalPosition().Sign() == quantity.Sign())
          .ToArray();
        if(aos.Any()) {
          aos.ForEach(ao => {
            Trace($"OpenTrade: {contract} already has active order {ao.order.OrderId} with status: {ao.status}.\nUpdating {new { price }}");
            UpdateOrder(ao.order.OrderId, OrderPrice(price, contract, minTickMultiplier));
          });
        } else {
          var orderType = price == 0 ? "MKT" : type.IfEmpty("LMT");
          bool isPreRTH = orderType == "LMT";
          var order = OrderFactory(contract, quantity, price, goodTillDate, minTickMultiplier, orderType, isPreRTH);
          var tpOrder = MakeOCOOrder(order);
          new[] { (order, contract, price), (tpOrder, contractOCO, 0) }
            .ForEach(o => {
              Handle110(o.contract, minTickMultiplier, o.order, o.price);
              orderContracts.Add(new OrdeContractHolder(o.order, o.contract));
              _verbous(new { plaseOrder = o });
              IbClient.ClientSocket.placeOrder(o.order.OrderId, o.contract, o.order);
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


    private void Handle110(Contract contract, int minTickMultiplier, Order order, double price)
      => IbClient.WatchReqError(() => order.OrderId, e => {
        OpenTradeError(contract, order, e, new { minTickMultiplier });
        if(e.errorCode == 110 && minTickMultiplier <= 5 && order.LmtPrice != 0) {
          order.LmtPrice = OrderPrice(price, contract, ++minTickMultiplier);
          order.OrderId = NetOrderId();
          Trace(new { replaceOrder = new { order, contract, price } });
          IbClient.ClientSocket.placeOrder(order.OrderId, contract, order);
        }
      }, () => Trace(new { order, Error = "done" }));

    void OpenTradeError(Contract c, IBApi.Order o, (int id, int errorCode, string errorMsg, Exception exc) t, object context) {
      var trace = $"{nameof(OpenTrade)}:{c}:" + (context == null ? "" : context + ":");
      var isWarning = Regex.IsMatch(t.errorMsg, @"\sWarning:") || t.errorCode == 103;
      if(!isWarning) OnOpenError(t, trace);
      else
        Trace(trace + t + "\n" + o);
    }
  }
}
