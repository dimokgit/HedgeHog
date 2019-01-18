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
      UseOrderContracts(ocs => ocs.RemoveAll(oc => oc.isNew));
    }
    static object _OpenTradeSync = new object();
    public static void FillAdaptiveParams(IBApi.Order baseOrder, string priority) {
      baseOrder.AlgoStrategy = "Adaptive";
      baseOrder.AlgoParams = new List<TagValue>();
      baseOrder.AlgoParams.Add(new TagValue("adaptivePriority", priority));
    }

    public void OpenLimitOrder(Contract contract, int quantity, double profit, bool useMarketPrice, bool useTakeProfit, int minTickMultiplier = 1, [CallerMemberName] string Caller = "") {
      double ask((double ask, double bid, DateTime time, double) p) => useMarketPrice ? p.ask : p.bid;
      double bid(double a, double b) => useMarketPrice ? b : a;
      IbClient.ReqPriceSafe(contract, 1, true).Select(p => quantity > 0 ? ask(p) : bid(p.ask, p.bid))
       .Subscribe(price => OpenTrade(contract, "", quantity, price, profit, useTakeProfit, DateTime.MaxValue, minTickMultiplier, Caller));
    }

    public PendingOrder OpenTrade(string pair, int quantity, double price, double profit, bool useTakeProfit, DateTime goodTillDate, int minTickMultiplier = 1, [CallerMemberName] string Caller = "") {
      if(!IBApi.Contract.Contracts.TryGetValue(pair, out var contract))
        throw new Exception($"Pair:{pair} is not fround in Contracts");
      return OpenTrade(contract, "", quantity, price, profit, useTakeProfit, goodTillDate, minTickMultiplier, Caller);
    }


    public PendingOrder OpenTrade(Contract contract, int quantity, double price = 0, double profit = 0, bool useTakeProfit = false) =>
      OpenTrade(contract, "", quantity, price, profit, useTakeProfit, DateTime.MaxValue);
    public PendingOrder OpenTrade(Contract contract, int quantity, double price, double profit, bool useTakeProfit, DateTime goodTillDate, DateTime googAfterDate) =>
      OpenTrade(contract, "", quantity, price, profit, useTakeProfit, goodTillDate, googAfterDate);

    public PendingOrder OpenTrade(Contract contract, int quantity, double price, double profit, bool useTakeProfit, DateTime goodTillDate, int minTickMultiplier = 1, [CallerMemberName] string Caller = "") =>
      OpenTrade(contract, "", quantity, price, profit, useTakeProfit, goodTillDate, minTickMultiplier, Caller);
    public PendingOrder OpenTrade(Contract contract, string type, int quantity, double price, double profit, bool useTakeProfit, DateTime goodTillDate, int minTickMultiplier = 1, [CallerMemberName] string Caller = "")
    => OpenTrade(contract, type, quantity, price, profit, useTakeProfit, goodTillDate, DateTime.MinValue, (OrderCondition)null, minTickMultiplier, Caller);
    public PendingOrder OpenTrade(Contract contract, int quantity, double price, double profit, bool useTakeProfit, DateTime goodTillDate, OrderCondition condition, int minTickMultiplier = 1, [CallerMemberName] string Caller = "") =>
      OpenTrade(contract, "", quantity, price, profit, useTakeProfit, goodTillDate, DateTime.MinValue, condition, minTickMultiplier, Caller);
    public PendingOrder OpenTrade(Contract contract, string type, int quantity, double price, double profit, bool useTakeProfit, DateTime goodTillDate, DateTime goodAfterDate, OrderCondition condition = null, int minTickMultiplier = 1, [CallerMemberName] string Caller = "") {
      var timeoutInMilliseconds = 5000;
      if(!Monitor.TryEnter(_OpenTradeSync, timeoutInMilliseconds)) {
        var message = new { contract, quantity, Method = nameof(OpenTrade), Caller, timeoutInMilliseconds } + "";
        Trace(new TimeoutException(message));
        return null;
      }
      var aos = OrderContractsInternal
        .Where(oc => !oc.isDone && oc.contract.Key == contract.Key && oc.order.TotalPosition().Sign() == quantity.Sign())
        .ToArray();
      if(aos.Any()) {
        aos.ForEach(ao => {
          Trace($"OpenTrade: {contract} already has active order {ao.order.OrderId} with status: {ao.status}.\nUpdating {new { price }}");
          UpdateOrder(ao.order.OrderId, OrderPrice(price, contract, minTickMultiplier));
        });
        ExitMomitor();
        return null;
      }
      var orderType = price == 0 ? "MKT" : type.IfEmpty("LMT");
      bool isPreRTH = true;// orderType == "LMT";
      var order = OrderFactory(contract, quantity, price, goodTillDate, goodAfterDate, minTickMultiplier, orderType, isPreRTH);
      if(condition != null) order.Conditions.Add(condition);
      //if(!contract.IsCombo && !contract.IsFutureOption)
      //  FillAdaptiveParams(order, "Normal");
      var tpOrder = (useTakeProfit ? MakeTakeProfitOrder(order, contract, profit, minTickMultiplier) : new (IBApi.Order order, double price)[0].ToObservable()).Select(x => new { x.order, x.price, useTakeProfit = false });
      new[] { new { order, price, useTakeProfit } }.ToObservable().Merge(tpOrder)
        .ToArray()
        .SelectMany(x => x)
        .Subscribe(o => {
          var reqId = o.order.OrderId;
          IbClient.WatchReqError(() => reqId, e => {
            OpenTradeError(contract, o.order, e, new { minTickMultiplier });
            if(e.errorCode == 110 && minTickMultiplier <= 5 && o.order.LmtPrice != 0) {
              o.order.LmtPrice = OrderPrice(o.price, contract, ++minTickMultiplier);
              reqId = o.order.OrderId = NetOrderId();
              Trace(new { replaceOrder = new { o, contract } });
              IbClient.ClientSocket.placeOrder(o.order.OrderId, contract, o.order);
            }
          }, () => Trace(new { o.order, Error = "done" }));
          OrderContractsInternal.Add(new OrdeContractHolder(o.order, contract));
          _verbous(new { plaseOrder = new { o, contract } });
          IbClient.ClientSocket.placeOrder(o.order.OrderId, contract, o.order);
        }, exc => ExitMomitor(), ExitMomitor);
      return null;
      /// Locals
      void ExitMomitor() {
        Trace($"{nameof(OpenTrade)}: exiting {nameof(_OpenTradeSync)} monitor");
        Monitor.Exit(_OpenTradeSync);
      }
    }
    private IBApi.Order OrderFactory(Contract contract, int quantity, double price, DateTime goodTillDate, DateTime goodAfterDate, int minTickMultiplier, string orderType, bool isPreRTH) {
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
      } else
        order.Tif = GTC;
      if(!goodAfterDate.IsMin())
        order.GoodAfterTime = goodAfterDate.ToTWSString();
      if(contract.Symbol.Contains(",")) {
        order.SmartComboRoutingParams = new List<TagValue>();
        order.SmartComboRoutingParams.Add(new TagValue("NonGuaranteed", "1"));
      }
      return order;
    }

    IObservable<(IBApi.Order order, double price)> MakeTakeProfitOrder(IBApi.Order parent, Contract contract, Contract under, double profit, int minTickMultilier) {
      bool isPreRTH = false;
      return new[] { parent }
      .Where(o => o.OrderType == "LMT")
      .Select(o => o.LmtPrice)
      .ToObservable()
      .Concat(Observable.Defer(() => IbClient.ReqPriceSafe(contract, 1, true).Select(p => parent.IsBuy() ? p.ask : p.bid)))
      //.OnEmpty(() => Trace($"No take profit order for {parent}"))
      .Select(lmtPrice => {
        parent.Transmit = false;
        var takeProfit = (profit >= 1 ? profit : lmtPrice * profit) * (parent.IsBuy() ? 1 : -1);
        var price = lmtPrice + takeProfit;
        var ret= (order:new IBApi.Order() {
          Account = _accountId,
          ParentId = parent.OrderId,
          LmtPrice = OrderPrice(price, contract, minTickMultilier),
          OrderId = NetOrderId(),
          Action = parent.Action == "BUY" ? "SELL" : "BUY",
          OrderType = "LMT",
          TotalQuantity = parent.TotalQuantity,
          Tif = GTC,
          OutsideRth = isPreRTH,
          OverridePercentageConstraints = true,
          Transmit = true
        }, price);
        Contract.FromCache("ESH9").ForEach(u => ret.order.Conditions.Add(u.PriceFactory(2600, false, false)));
        return ret;
      })
      .Take(1)
      .OnEmpty(() => Trace($"No take profit order for {parent}"))
        ;
    }

    public void OpenRollTrade(string currentSymbol, string rollSymbol) {
      (from cc in IbClient.ReqContractDetailsCached(currentSymbol).Select(cd => cd.Contract)
       from rc in IbClient.ReqContractDetailsCached(rollSymbol).Select(cd => cd.Contract)
       from ct in ComboTrades(5)
       where ct.contract.ConId == cc.ConId
       select (cc, rc, ct)
       )
        .Subscribe(t => {
          var tradeDate = IbClient.ServerTime.Date.AddHours(15).AddMinutes(45);
          if(t.cc.IsOption)
            CreateRoll(currentSymbol, rollSymbol)
              .Subscribe(rc => OpenTrade(rc.rollContract, -rc.currentTrade.position, 0, 0, false, DateTime.MaxValue,tradeDate.TimeCondition()));
          else
            OpenTrade(t.rc, -t.ct.position.Abs(), 0, 0, false, DateTime.MaxValue, tradeDate);
        });
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
