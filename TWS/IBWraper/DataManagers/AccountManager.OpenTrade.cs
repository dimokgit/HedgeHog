using HedgeHog;
using HedgeHog.Shared;
using IBApi;
using IBSampleApp.messages;
using System;
using System.Collections.Concurrent;
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
    private int NetOrderId() => IbClient.ValidOrderId();
    public IObservable<(OrderContractHolder holder, ErrorMessage error)[]> OpenTradeWhatIf(string pair, bool buy) {
      var anount = GetTrades().Where(t => t.Pair == pair).Select(t => t.GrossPL).DefaultIfEmpty(Account.Equity).Sum() / 2;
      if(!IBApi.Contract.Contracts.TryGetValue(pair, out var contract))
        throw new Exception($"Pair:{pair} is not fround in Contracts");
      return OpenTrade(contract, contract.IsFuture ? 1 : 100);
    }
    public void OpenOrUpdateLimitOrderByProfit2(string instrument, int position, int orderId, double openAmount, double profitAmount) {
      var pa = profitAmount >= 1 ? profitAmount : openAmount.Abs() * profitAmount;
      OrderContractsInternal.Values.ByOrderId(orderId)
      .Where(och => !och.isDone)
      .Do(och => {
        if(och.contract.Instrument != instrument)
          throw new Exception($"{nameof(OpenOrUpdateLimitOrderByProfit2)}:{new { orderId, och.contract.Instrument, dontMatch = instrument }}");
        var limit = OrderPrice(priceFromProfit(pa, position, och.contract.ComboMultiplier, openAmount), och.contract);
        UpdateOrder(orderId, limit);
      })
      .RunIfEmpty(() => { // Create new order
        Contract.FromCache(instrument)
        .Count(1, new { OpenOrUpdateOrder = new { instrument, unexpected = "count in cache" } })
        .ForEach(c => {
          var lmtPrice = OrderPrice(priceFromProfit(pa, position, c.ComboMultiplier, openAmount), c);
          OpenTrade(c, -position, lmtPrice, 0.0, false, DateTime.MaxValue);
        });
      });
    }
    public void OpenOrUpdateLimitOrderByProfit3(Contract contract, int position, int orderId, double openPrice, double profitAmount) {
      var limit = profitAmount >= 1 ? profitAmount / contract.PipCost() * position : openPrice * profitAmount;
      OpenOrUpdateLimitOrder(contract, position, orderId, openPrice + limit);
    }
    public void OpenOrUpdateLimitOrder(Contract contract, int position, int orderId, double lmpPrice) {
      UseOrderContracts(orderContracts =>
        orderContracts.ByOrderId(orderId)
        .Where(och => !och.isDone)
        .Do(och => {
          if(och.contract.Instrument != contract.Instrument)
            throw new Exception($"{nameof(OpenOrUpdateLimitOrder)}:{new { orderId, och.contract.Instrument, dontMatch = contract.Instrument }}");
          UpdateOrder(orderId, OrderPrice(lmpPrice, och.contract));
        })
        .RunIfEmpty(() => OpenTrade(contract, -position, lmpPrice, 0.0, false, DateTime.MaxValue)
      ));
    }
    public void CancelOrder(int orderId) => IbClient.ClientSocket.cancelOrder(orderId);
    public void UpdateOrder(int orderId, double lmpPrice, int minTickMultiplier = 1) {
      UseOrderContracts(orderContracts => {
        var och = orderContracts.ByOrderId(orderId).SingleOrDefault();
        if(och == null)
          throw new Exception($"UpdateTrade: {new { orderId, not = "found" }}");
        if(och.isDone)
          throw new Exception($"UpdateTrade: {new { orderId, och.isDone }}");
        if(och.order.IsLimit && lmpPrice == 0) {
          Trace($"{nameof(UpdateOrder)}: cancelling pending {new { och.order, och.contract }}");
          IbClient.ClientSocket.cancelOrder(orderId);
          return;
        }
        var order = och.order;
        //var minTick = och.contract.MinTick;
        order.LmtPrice = OrderPrice(lmpPrice, och.contract);//  Math.Round(lmpPrice / minTick) * minTick;
        if(order.OpenClose.IsNullOrWhiteSpace())
          order.OpenClose = "C";
        order.VolatilityType = 0;
        IbClient.WatchReqError(orderId, e => {
          OnOpenError(e, $"{nameof(UpdateOrder)}:{och.contract}:{new { order.LmtPrice }}");
          if(e.errorCode == E110)
            UpdateOrder(orderId, lmpPrice, ++minTickMultiplier);
        }, () => { });
        IbClient.ClientSocket.placeOrder(order.OrderId, och.contract, order);
      });
    }
    private void OnOpenError((int reqId, int code, string error, Exception exc) e, string trace) {
      Trace(trace + e);
      OrderContractsInternal.Values.ByOrderId(e.reqId).ToList().ForEach(oc => {
        if(new[] { 200, 201, 203, 321, 382, 383 }.Contains(e.code)) {
          //OrderStatuses.TryRemove(oc.contract?.Symbol + "", out var os);
          RaiseOrderRemoved(oc);
        }
      });
    }

    ConcurrentDictionary<string,(object l,bool b)> _placingOrderLocker = new ConcurrentDictionary<string, (object l, bool b)>();
    public IObservable<ErrorMessage<IEnumerable<OrderContractHolder>>> PlaceOrder(IBApi.Order order, Contract contract) {
      var key = order + "" + contract;
      var locker = _placingOrderLocker.GetOrAdd(key, (new object(),false));
      lock(locker.l) {
        if(locker.b) return Observable.Return(ErrorMessage.Create(Default(), new ErrorMessage(0, 0, locker + "", new PlaceOrderException())));
        locker.b = true;
        if(order.OrderId == 0)
          order.OrderId = NetOrderId();
        var oso = OrderStatusObservable;
        var wte = IbClient.WireToErrorMessage(order.OrderId, oso, m => m.OrderId
        , m => OrderContractsInternal.ByOrderId(m.OrderId)
        , Default
        , e => OpenTradeError(contract, order, e, new { }));
        IbClient.ClientSocket.placeOrder(order.OrderId, contract, order);
        return wte.FirstAsync().Do(_ => locker.b = false);
      }
      IEnumerable<OrderContractHolder> Default() { yield break; }
    }
    public PendingOrder OpenSpreadTrade((string pair, bool buy, int lots)[] legs, double takeProfit, double stopLoss, string remark, bool whatIf) {
      UseOrderContracts(orderContracts => {
        var isStock = legs.All(l => l.pair.IsUSStock());
        var legs2 = legs.Select(t => (t.pair, t.buy, t.lots, price: IbClient.GetPrice(t.pair))).ToArray();
        var price = legs2[0].price;
        var rth = Lazy.Create(() => new[] { price.Time.Date.AddHours(9.5), price.Time.Date.AddHours(16) });
        var isPreRTH = !whatIf && isStock && !price.Time.Between(rth.Value);
        var orderType = "MKT";
        var c = ContractSamples.StockComboContract();
        var o = new IBApi.Order() {
          Account = _accountId,
          OrderId = NetOrderId(),
          Action = legs[0].buy ? "BUY" : "SELL",
          OrderType = orderType,
          TotalQuantity = legs[0].lots,
          Tif = GTC,
          OutsideRth = isPreRTH,
          WhatIf = whatIf,
          OverridePercentageConstraints = true
        };
        _verbous(new { plaseOrder = new { o, c } });
        IbClient.ClientSocket.placeOrder(o.OrderId, c, o);
      });
      return null;
    }
    double OrderPrice(double orderPrice, Contract contract) {
      var minTick = contract.MinTick();
      var p = (Math.Round(orderPrice / minTick) * minTick);
      p = Math.Round(p, 4);
      return p;
    }

    public void CancelAllOrders(string message) {
      Trace($"{nameof(CancelAllOrders)}: {message}");
      IbClient.ClientSocket.reqGlobalCancel();
      UseOrderContracts(ocs => ocs.Values.Where(oc => oc.isNew).ForEach(_ => ocs.TryRemove(_.order.OrderId, out var __)));
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
       let price = isSell ? p.bid : p.ask
       from po in OpenTrade(c, quantity, hasCondition ? 0 : price, 0, true, DateTime.MaxValue, dateAfter
       , hasCondition ? uc.PriceCondition(conditionPrice.Value, isSell && c.IsCall, false) : null, condProfit, "")
       select po
       )
       .Subscribe();
    }

    private readonly SemaphoreSlim _mutexOpenTrade = new SemaphoreSlim(1);
    public IObservable<(OrderContractHolder holder, ErrorMessage error)[]> OpenTrade(Contract contract, int quantity, double price = 0, double profit = 0, bool useTakeProfit = false, DateTime goodTillDate = default, DateTime goodAfterDate = default, OrderCondition condition = null, OrderCondition takeProfitCondition = null, string type = "", [CallerMemberName] string Caller = "") {
      var aos = OrderContractsInternal.Values
        .Where(oc => !oc.isDone && oc.contract == contract && oc.order.TotalPosition().Sign() == quantity.Sign())
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
      if(false && !contract.IsCombo && !contract.IsFutureOption) FillAdaptiveParams(order, "Normal");
      var tpOrder = (useTakeProfit
        ? MakeTakeProfitOrder2(order, contract, takeProfitCondition, profit)
        : new (IBApi.Order order, double price)[0].ToObservable()
        ).Select(x => new { x.order, x.price });
      var orders = new[] { new { order, price } }.ToObservable().Merge(tpOrder).ToArray();
      var obss = (from os in orders
                  from o in os
                  from po in PlaceOrder(o.order, contract).Take(1)
                  from value in po.value.DefaultIfEmpty()
                  select (value, po.error)).ToArray();
      return obss.Do(a => a.Select(_ => $"{new { reqId = _.value?.order?.OrderId ?? _.error.reqId, _.value?.order, _.error }}")
        .Concat(new[] { nameof(OrderContractsInternal) + ":" })
        .Concat(OrderContractsInternal.Select(oc => oc + "")).Flatter("\n"));
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
