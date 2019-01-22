using HedgeHog;
using HedgeHog.Shared;
using IBApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
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
    public static void FillAdaptiveParams(IBApi.Order baseOrder, string priority) {
      baseOrder.AlgoStrategy = "Adaptive";
      baseOrder.AlgoParams = new List<TagValue>();
      baseOrder.AlgoParams.Add(new TagValue("adaptivePriority", priority));
    }

    public void OpenLimitOrder(Contract contract, int quantity, double profit, bool useMarketPrice, bool useTakeProfit) {
      double ask((double ask, double bid, DateTime time, double) p) => useMarketPrice ? p.ask : p.bid;
      double bid(double a, double b) => useMarketPrice ? b : a;
      IbClient.ReqPriceSafe(contract, 1, true).Select(p => quantity > 0 ? ask(p) : bid(p.ask, p.bid))
       .Subscribe(price => OpenTrade(contract, quantity, price, profit, useTakeProfit));
    }

    public void OpenTradeWithConditions(string symbol, int quantity, double profit, double? conditionPrice, bool _isTest) {
      var isSell = quantity < 0;
      var isBuy = quantity > 0;
      var hasCondition = conditionPrice.HasValue;
      var needLimitPrice = !hasCondition;
      var dateAfter = _isTest ? DateTime.Now.Date.AddDays(3) : DateTime.MinValue;
      (from c in IbClient.ReqContractDetailsCached(symbol).Select(cd => cd.Contract)
       from uc in c.UnderContract
       let isMore = c.IsCall && isBuy || c.IsPut && isSell
       let upProfit = profit * (isMore ? 1 : -1)
       from p in needLimitPrice ? IbClient.ReqPriceSafe(c, 1, true) : IbClient.ReqPriceEmpty()
       from up in conditionPrice.HasValue ? new[] { conditionPrice.Value }.ToObservable() : IbClient.ReqPriceSafe(uc, 1, true).Select(p => p.ask.Avg(p.bid))
       let condProfit = uc.PriceCondition(up + upProfit, isMore, false)
       select new { c, price = isSell ? p.bid : p.ask, uc, condProfit }
       )
       .Subscribe(t => OpenTrade(t.c, quantity, hasCondition ? 0 : t.price, 0, true, DateTime.MaxValue, dateAfter
       , hasCondition ? t.uc.PriceCondition(conditionPrice.Value, isSell && t.c.IsCall, false) : null, t.condProfit, ""));
    }

    private readonly SemaphoreSlim _mutexOpenTrade = new SemaphoreSlim(1);
    public PendingOrder OpenTrade(Contract contract, int quantity, double price = 0, double profit = 0, bool useTakeProfit = false, DateTime goodTillDate = default, DateTime goodAfterDate = default, OrderCondition condition = null, OrderCondition takeProfitCondition = null, string type = "", [CallerMemberName] string Caller = "") {
      var timeoutInMilliseconds = 5000;
      var inMutex = false;
      if(!_mutexOpenTrade.Wait(timeoutInMilliseconds)) {
        var message = new { contract, quantity, Method = nameof(OpenTrade), Caller, timeoutInMilliseconds } + "";
        Trace(new TimeoutException(message));
        return null;
      }
      inMutex = true;
      var aos = OrderContractsInternal
        .Where(oc => !oc.isDone && oc.contract.Key == contract.Key && oc.order.TotalPosition().Sign() == quantity.Sign())
        .ToArray();
      if(aos.Any()) {
        aos.ForEach(ao => {
          Trace($"OpenTrade: Cancelling existing {new { ao.order }} for {new { contract }} with {new { ao.status }}.");
          CancelOrder(ao.order.OrderId);
        });
      }
      var orderType = price == 0 ? "MKT" : type.IfEmpty("LMT");
      bool isPreRTH = true;// orderType == "LMT";
      var minTickMultiplier = contract.MinTick();
      var order = OrderFactory(contract, quantity, price, goodTillDate, goodAfterDate, orderType, isPreRTH);
      if(condition != null) order.Conditions.Add(condition);
      //if(!contract.IsCombo && !contract.IsFutureOption)
      //  FillAdaptiveParams(order, "Normal");
      var tpOrder = (useTakeProfit ? MakeTakeProfitOrder2(order, contract, takeProfitCondition, profit) : new (IBApi.Order order, double price)[0].ToObservable()).Select(x => new { x.order, x.price, useTakeProfit = false });
      var orders = new[] { new { order, price, useTakeProfit } }.ToObservable().Merge(tpOrder).ToArray();
      var obs = orders
      .SelectMany(x => x)
      .SelectMany(o => {
        var reqId = o.order.OrderId;
        var eo = IbClient.ReqError(() => reqId
        , e => {
          if(!OpenTradeError(contract, o.order, e, new { }))
            OrderContractsInternal.RemoveAll(x => x.order.OrderId == e.id);
        });
        //, () => { Trace(new { o.order, Error = "done" }); });
        OrderContractsInternal.Add(new OrdeContractHolder(o.order, contract));
        _verbous(new { plaseOrder = new { o, contract } });
        var po = PlaceOrder(o.order, contract)
        .Take(1)
        .Select(y => (id: y.OrderId, errorCode: 0, errorMsg: "", exc: (Exception)null));
        return eo.Merge(po)
        .Take(1);
      });
      obs
        .Take(useTakeProfit ? 2 : 1)
        .ToArray()
        //.Aggregate(new[] { (id: 0, errorCode: 0, errorMsg: "", exc: (Exception)null) }.Take(0), (a, b) => a.Concat(new[] { b }))
        .Subscribe(a => ExitMomitor($"{nameof(OpenTrade)} is done for:\n"
        + a.Select(_ => $"{new { _.id, _.errorCode, _.errorMsg }}")
        .Concat(new[] { nameof(OrderContractsInternal) + ":" })
        .Concat(OrderContractsInternal.Select(oc => oc + "")).Flatter("\n")));
      return null;
      /// Locals
      void ExitMomitor<T>(T context) {
        if(inMutex) {
          Trace($"{nameof(_mutexOpenTrade)}:{nameof(OpenTrade)}: exiting  monitor. {context}");
          _mutexOpenTrade.Release();
        }
      }
    }
    private IBApi.Order OrderFactory(Contract contract, int quantity, double price, DateTime goodTillDate, DateTime goodAfterDate, string orderType, bool isPreRTH) {
      var order = new IBApi.Order() {
        Account = _accountId,
        OrderId = NetOrderId(),
        Action = quantity > 0 ? "BUY" : "SELL",
        OrderType = orderType,
        LmtPrice = OrderPrice(price, contract),
        TotalQuantity = quantity.Abs(),
        OutsideRth = isPreRTH,
        OverridePercentageConstraints = true
      };
      if(goodTillDate != default && !goodTillDate.IsMax()) {
        order.Tif = GTD;
        order.GoodTillDate = goodTillDate.ToTWSString();
      } else
        order.Tif = GTC;
      if(goodAfterDate != default)
        order.GoodAfterTime = goodAfterDate.ToTWSString();
      if(contract.Symbol.Contains(",")) {
        order.SmartComboRoutingParams = new List<TagValue>();
        order.SmartComboRoutingParams.Add(new TagValue("NonGuaranteed", "1"));
      }
      return order;
    }

    IObservable<(IBApi.Order order, double price)> MakeTakeProfitOrder(IBApi.Order parent, Contract contract, double profit) {
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
        var ret = (order: new IBApi.Order() {
          Account = _accountId,
          ParentId = parent.OrderId,
          LmtPrice = OrderPrice(price, contract),
          OrderId = NetOrderId(),
          Action = parent.Action == "BUY" ? "SELL" : "BUY",
          OrderType = "LMT",
          TotalQuantity = parent.TotalQuantity,
          Tif = GTC,
          OutsideRth = isPreRTH,
          OverridePercentageConstraints = true,
          Transmit = true
        }, price);
        Contract.FromCache("ESH9").ForEach(u => ret.order.Conditions.Add(u.PriceCondition(2600, false, false)));
        return ret;
      })
      .Take(1)
      .OnEmpty(() => Trace($"No take profit order for {parent}"))
        ;
    }
    IObservable<(IBApi.Order order, double price)> MakeTakeProfitOrder2(IBApi.Order parent, Contract contract, OrderCondition takeProfitCondition, double profit) {
      var takeProfitPrice = (takeProfitCondition as PriceCondition)?.Price;
      bool isPreRTH = false;
      var isMarket = takeProfitPrice.HasValue;
      return new[] { parent }
      .Where(o => takeProfitPrice.HasValue || o.OrderType == "LMT")
      .Select(o => o.LmtPrice)
      .ToObservable()
      .Concat(Observable.Defer(()
      => IbClient.ReqPriceSafe(contract, 1, true).Select(p => parent.IsBuy() ? p.ask : p.bid).SubscribeOn(Scheduler.CurrentThread)
      ).SubscribeOn(Scheduler.CurrentThread)
      ).SubscribeOn(Scheduler.CurrentThread)
      //.OnEmpty(() => Trace($"No take profit order for {parent}"))
      .Select(lmtPrice => {
        parent.Transmit = false;
        var takeProfit = (profit >= 1 ? profit : lmtPrice * profit) * (parent.IsBuy() ? 1 : -1);
        var price = lmtPrice + takeProfit;
        var order = new IBApi.Order() {
          Account = _accountId,
          ParentId = parent.OrderId,
          LmtPrice = isMarket ? 0 : OrderPrice(price, contract),
          OrderId = NetOrderId(),
          Action = parent.Action == "BUY" ? "SELL" : "BUY",
          OrderType = isMarket ? "MKT" : "LMT",
          TotalQuantity = parent.TotalQuantity,
          Tif = GTC,
          OutsideRth = isPreRTH,
          OverridePercentageConstraints = true,
          Transmit = true
        };
        order.Conditions.Add(takeProfitCondition);
        return (order, price);
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
              .Subscribe(rc => OpenTrade(rc.rollContract, -rc.currentTrade.position, 0, 0, false, default, default, tradeDate.TimeCondition()));
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
