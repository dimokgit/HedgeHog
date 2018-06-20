using HedgeHog;
using HedgeHog.Shared;
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
    public void CancelAllOrders(string message) {
      Trace($"{nameof(CancelAllOrders)}: {message}");
      IbClient.ClientSocket.reqGlobalCancel();
    }
    static object _OpenTradeSync = new object();
    public static void FillAdaptiveParams(IBApi.Order baseOrder, string priority) {
      baseOrder.AlgoStrategy = "Adaptive";
      baseOrder.AlgoParams = new List<TagValue>();
      baseOrder.AlgoParams.Add(new TagValue("adaptivePriority", priority));
    }

    public void OpenLimitOrder(Contract contract, int quantity, bool useTakeProfit, int minTickMultiplier = 1, [CallerMemberName] string Caller = "") {
      IbClient.ReqPriceSafe(contract, 1, true).Select(p => quantity > 0 ? p.ask : p.bid)
       .Subscribe(price => OpenTrade(contract, "", quantity, price, useTakeProfit, DateTime.MaxValue, minTickMultiplier, Caller));
    }
    public PendingOrder OpenTrade(Contract contract, int quantity, double price, bool useTakeProfit, DateTime goodTillDate, int minTickMultiplier = 1, [CallerMemberName] string Caller = "") =>
      OpenTrade(contract, "", quantity, price, useTakeProfit, goodTillDate, minTickMultiplier, Caller);
    public PendingOrder OpenTrade(Contract contract, string type, int quantity, double price, bool useTakeProfit, DateTime goodTillDate, int minTickMultiplier = 1, [CallerMemberName] string Caller = "") {
      var timeoutInMilliseconds = 5000;
      if(!Monitor.TryEnter(_OpenTradeSync, timeoutInMilliseconds)) {
        var message = new { contract, quantity, Method = nameof(OpenTrade), Caller, timeoutInMilliseconds } + "";
        Trace(new TimeoutException(message));
        return null;
      }
      var aos = OrderContractsInternal.Values
        .Where(oc => !oc.isDone && oc.contract.Key == contract.Key && oc.order.TotalPosition().Sign() == quantity.Sign())
        .ToArray();
      if(aos.Any()) {
        aos.ForEach(ao => {
          Trace($"OpenTrade: {contract} already has active order order with status: {ao.status}.\nUpdating {new { price }}");
          //UpdateOrder(ao.order.OrderId, OrderPrice(price, contract, minTickMultiplier));
        });
        ExitMomitor();
        return null;
      }
      var orderType = price == 0 ? "MKT" : type.IfEmpty("LMT");
      bool isPreRTH = orderType == "LMT";
      var order = new IBApi.Order() {
        Account = _accountId,
        OrderId = NetOrderId(),
        Action = quantity > 0 ? "BUY" : "SELL",
        OrderType = orderType,
        LmtPrice = OrderPrice(price, contract, minTickMultiplier),
        TotalQuantity = quantity.Abs(),
        OutsideRth = isPreRTH,
        OverridePercentageConstraints = true
      };
      if(!goodTillDate.IsMax()) {
        order.Tif = GTD;
        order.GoodTillDate = goodTillDate.ToTWSString();
      }else
        order.Tif = GTC;
      //if(!contract.IsCombo && !contract.IsFutureOption)
      //  FillAdaptiveParams(order, "Normal");
      var tpOrder = (useTakeProfit ? MakeTakeProfitOrder(order, contract, minTickMultiplier) : new(IBApi.Order order, double price)[0].ToObservable()).Select(x => new { x.order, x.price, useTakeProfit = false });
      new[] { new { order, price, useTakeProfit } }.ToObservable().Merge(tpOrder)
        .ToArray()
        .SelectMany(x => x)
        .Subscribe(o => {
          var reqId = o.order.OrderId;
          IbClient.WatchReqError(() => reqId, e => {
            Error(contract, o.order, e, new { minTickMultiplier });
            if(e.errorCode == 110 && minTickMultiplier <= 5 && o.order.LmtPrice != 0) {
              OrderContractsInternal.TryRemove(reqId, out var old);
              o.order.LmtPrice = OrderPrice(o.price, contract, ++minTickMultiplier);
              reqId = o.order.OrderId = NetOrderId();
              Trace(new { replaceOrder = new { o, contract } });
              OrderContractsInternal.TryAdd(o.order.OrderId, new OrdeContractHolder(o.order, contract));
              IbClient.ClientSocket.placeOrder(o.order.OrderId, contract, o.order);
              //OpenTrade(contract, quantity, price, o.useTakeProfit, ++minTickMultiplier);
            }
          }, () => Trace(new { o.order, Error = "done" }));
          OrderContractsInternal.TryAdd(o.order.OrderId, new OrdeContractHolder(o.order, contract));
          _verbous(new { plaseOrder = new { o, contract } });
          IbClient.ClientSocket.placeOrder(o.order.OrderId, contract, o.order);
        }, exc => ExitMomitor(), ExitMomitor);
      return null;
      /// Locals
      void ExitMomitor() {
        Trace($"{nameof(OpenTrade)}: exiting {nameof(_OpenTradeSync)} monitor");
        Monitor.Exit(_OpenTradeSync);
      }
      void Error(Contract c, IBApi.Order o, (int id, int errorCode, string errorMsg, Exception exc) t, object context) {
        var trace = $"{nameof(OpenTrade)}:{c}:" + (context == null ? "" : context + ":");
        var isWarning = Regex.IsMatch(t.errorMsg, @"\sWarning:") || t.errorCode == 103;
        if(!isWarning) OnOpenError(t, trace);
        else
          Trace(trace + t + "\n" + o);
      }
    }
    IObservable<(IBApi.Order order, double price)> MakeTakeProfitOrder(IBApi.Order parent, Contract contract, int minTickMultilier) {
      bool isPreRTH = false;
      return new[] { parent }
      .Where(o => o.OrderType == "LMT")
      .Select(o => o.LmtPrice)
      .ToObservable()
      .Concat(Observable.Defer(() => IbClient.ReqPriceSafe(contract, 1, true).Select(p => parent.IsBuy() ? p.ask : p.bid)))
      //.OnEmpty(() => Trace($"No take profit order for {parent}"))
      .Select(lmtPrice => {
        parent.Transmit = false;
        var takeProfit = lmtPrice * 0.2 * (parent.IsBuy() ? 1 : -1);
        var price = lmtPrice + takeProfit;
        return (new IBApi.Order() {
          Account = _accountId,
          ParentId = parent.OrderId,
          LmtPrice = OrderPrice(price, contract, minTickMultilier),
          OrderId = NetOrderId(),
          Action = parent.Action == "BUY" ? "SELL" : "BUY",
          OrderType = "LMT",
          TotalQuantity = parent.TotalQuantity,
          Tif = "GTC",
          OutsideRth = isPreRTH,
          OverridePercentageConstraints = true,
          Transmit = true
        }, price);
      })
      .Take(1)
      .OnEmpty(() => Trace($"No take profit order for {parent}"))
        ;
    }

    //private void OnUpdateError(int reqId, int code, string error, Exception exc) {
    //  UseOrderContracts(orderContracts => {
    //    if(!orderContracts.TryGetValue(reqId, out var oc)) return;
    //    if(new[] { /*103, 110,*/ 200, 201, 202, 203, 382, 383 }.Contains(code)) {
    //      //OrderStatuses.TryRemove(oc.contract?.Symbol + "", out var os);
    //      Trace($"{nameof(OnUpdateError)}: {new { reqId, code, error }}");
    //      RaiseOrderRemoved(oc);
    //      orderContracts.TryRemove(reqId, out var oc2);
    //    }
    //    switch(code) {
    //      case 404:
    //        var contract = oc.contract + "";
    //        var order = oc.order + "";
    //        _verbous(new { contract, code, error, order });
    //        _defaultMessageHandler("Request Global Cancel");
    //        CancelAllOrders("Request Global Cancel");
    //        break;
    //    }
    //  });
    //}
  }
}
