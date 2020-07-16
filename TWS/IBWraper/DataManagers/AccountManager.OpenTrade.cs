using HedgeHog;
using HedgeHog.Shared;
using IBApi;
using IBSampleApp.messages;
using MarkdownLog;
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
using static IBApi.Order;
using ORDEREXT = System.Func<IBApi.Order, System.IObservable<IBApp.OrderPriceContract>>;
namespace IBApp {
  public partial class AccountManager {
    private int NetOrderId() => IbClient.ValidOrderId();
    public IObservable<OrderContractHolderWithError[]> OpenTradeWhatIf(string pair, bool buy) {
      var anount = GetTrades().Where(t => t.Pair == pair).Select(t => t.GrossPL).DefaultIfEmpty(Account.Equity).Sum() / 2;
      if(!IBApi.Contract.Contracts.TryGetValue(pair, out var contract))
        throw new Exception($"Pair:{pair} is not fround in Contracts");
      return OpenTrade(contract, contract.IsFuture ? 1 : 100);
    }
    public void OpenOrUpdateLimitOrderByProfit2(string key, int position, int orderId, double openAmount, double profitAmount) {
      var pa = profitAmount.Abs() >= 1 ? profitAmount : openAmount.Abs() * profitAmount;
      var contract = Contract.FromCache(key).ToArray();
      OrderContractsInternal.Items.ByOrderId(orderId)
      .Where(och => !och.isDone)
      .Do(och => {
        if(och.contract != contract.Single())
          throw new Exception($"{nameof(OpenOrUpdateLimitOrderByProfit2)}:{new { orderId, och.contract.Instrument, dontMatch = key }}");
        var limit = OrderPrice(priceFromProfit(pa, position, och.contract.ComboMultiplier, openAmount), och.contract);
        UpdateOrder(orderId, limit);
      })
      .RunIfEmpty(() => { // Create new order
        contract
        .ForEach(c => {
          var lmtPrice = OrderPrice(priceFromProfit(pa, position, c.ComboMultiplier, openAmount), c);
          OpenTrade(c, -position, lmtPrice, 0.0, false, DateTime.MaxValue).Subscribe();
        });
      });
    }
    public void OpenOrUpdateLimitOrderByProfit3(Contract contract, int position, int orderId, double openPrice, double profitAmount) {
      var limit = profitAmount.Abs() >= 1 ? profitAmount / contract.PipCost() * position : openPrice * profitAmount;
      OpenOrUpdateLimitOrder(contract, position, orderId, openPrice + limit);
    }
    public void OpenOrUpdateLimitOrder(Contract contract, int position, int orderId, double lmpPrice) {
      UseOrderContracts(orderContracts =>
        orderContracts.ByOrderId(orderId)
        .Where(och => !och.isDone && och.order.IsLimit)
        .Do(och => {
          if(och.contract.Instrument != contract.Instrument)
            throw new Exception($"{nameof(OpenOrUpdateLimitOrder)}:{new { orderId, och.contract.Instrument, dontMatch = contract.Instrument }}");
          UpdateOrder(orderId, OrderPrice(lmpPrice, och.contract));
        })
        .RunIfEmpty(() => OpenTrade(contract, -position, lmpPrice, 0.0, false, DateTime.MaxValue).Subscribe()
      ));
    }
    public void UpdateOrder(int orderId, double lmpPrice, int minTickMultiplier = 1) {
      UseOrderContracts(orderContracts => {
        var och = orderContracts.ByOrderId(orderId).SingleOrDefault();
        if(och == null)
          throw new Exception($"UpdateTrade: {new { orderId, not = "found" }}");
        if(och.isDone)
          throw new Exception($"UpdateTrade: {new { orderId, och.isDone }}");
        if(och.order.IsLimit && lmpPrice == 0) {
          Trace($"{nameof(UpdateOrder)}: cancelling pending {new { och.order, och.contract }}");
          IbClient.OnReqMktData(() => IbClient.ClientSocket.cancelOrder(orderId));
          return;
        }
        var order = och.order;
        //var minTick = och.contract.MinTick;
        order.SetLimit(OrderPrice(lmpPrice, och.contract));//  Math.Round(lmpPrice / minTick) * minTick;
        if(order.OpenClose.IsNullOrWhiteSpace())
          order.OpenClose = "C";
        order.VolatilityType = 0;
        order.Conditions = new List<OrderCondition>();
        IbClient.WatchReqError(1.FromSeconds(), orderId, e => {
          OnOpenError(e, $"{nameof(UpdateOrder)}:{och.contract}:{new { order.LmtPrice }}");
          if(e.errorCode == E110)
            UpdateOrder(orderId, lmpPrice, ++minTickMultiplier);
        }, () => { });
        IbClient.OnReqMktData(() => IbClient.ClientSocket.placeOrder(order.OrderId, och.contract, order));
      });
    }
    private void OnOpenError((int reqId, int code, string error, Exception exc) e, string trace) {
      TraceError(trace + e);
      OrderContractsInternal.Items.ByOrderId(e.reqId)
        .Where(_ => new[] { 200, 201, 203, 321, 382, 383 }.Contains(e.code))
        .ToList().ForEach(OrderContractsInternal.RemoveByHolder);
    }

    ConcurrentDictionary<string, bool> _placedOrders = new ConcurrentDictionary<string, bool>();
    object _placeOrderLocker = new object();
    public IObservable<ErrorMessages<OrderContractHolder>> PlaceOrder(IBApi.Order order, Contract contract) {
      var key = $"{ order.ActionText}::{contract.Key}::{order.TypeText}";
      lock(_placeOrderLocker) {
        var locker = _placedOrders.GetOrAdd(key, false);
        if(locker) {
          Verbose($"PlaceOrder: {new { key, exists = true }}");
          return Observable.Return(ErrorMessage.Create(Default(), new ErrorMessage(order.OrderId, 0, new { key } + "", new PlaceOrderException())));
        }
        _placedOrders.TryUpdate(key, true, false);
        TraceDebug($"locker [{order.OrderId}] => {_placedOrders[key]} for {key}");
        if(order.OrderId == 0)
          order.OrderId = NetOrderId();
        {// Fix order
          order.Volatility = double.MaxValue;
          order.VolatilityType = int.MaxValue;
          if(order.LmtPrice.IsSetAndNotZero())
            order.LmtPrice = OrderPrice(order.LmtPrice, contract);
        }
        var oso = OpenOrderObservable;
        //if(true || !order.Transmit)          oso = oso.TakeUntil(Observable.Timer(TimeSpan.FromSeconds(1))).Spy("PlaceOrder Timer", t => TraceDebug(t));//.DefaultIfEmpty();
        int GetOtderId(OpenOrderMessage m) => m == null ? order.OrderId : m.OrderId;
        var wte = IbClient.WireToErrorMessage(order.OrderId, oso, GetOtderId
        , m => OrderContractsInternal.Items.ByOrderId(GetOtderId(m))
        , Default
        , e => OpenTradeError(contract, order, e, new { }));
        IbClient.OnReqMktData(() => IbClient.ClientSocket.placeOrder(order.OrderId, contract, order));
        return wte
          .TakeUntil(Observable.Timer(TimeSpan.FromSeconds(1)))
          .ToArray()
          .FirstAsync()
          .SelectMany(ems => {
            TraceDebug($"locker [{order.OrderId}] <= {_placedOrders[key]} for {key}");
            _placedOrders.TryRemove(key, out var _);
            if(ems.IsEmpty()) {
              Trace($"PlaceOrder[{order.OrderId}] for {key} returned empty handed. Callint reqOpenOrders.");
              IbClient.OnReqMktData(() => IbClient.ClientSocket.reqOpenOrders());
              return OpenOrderObservable
              .Where(m => m.OrderId == order.OrderId)
              .TakeUntil(Observable.Timer(TimeSpan.FromSeconds(1)))
              .Take(1)
              .Select(m => ErrorMessage.Empty(OrderContractsInternal.Items.ByOrderId(GetOtderId(m))));
            }
            return ems.ToObservable();
          })
          .Select(och => och.value.IsEmpty() ? ErrorMessage.Empty(new[] { new OrderContractHolder(order, contract, "PreTransmitted") }.AsEnumerable()) : och);
      }
      IEnumerable<OrderContractHolder> Default() { yield return new OrderContractHolder(order, contract, default); }
    }
    public IObservable<ErrorMessages<OrderContractHolder>> CancelOrder(int orderId) {
      const int ORDER_TOCANCEL_NOTFOUND = 10147;
      var o = OrderContractsInternal.Items.ByOrderId(orderId).SingleOrDefault(oh => oh.isInactive);
      if(o != null) {
        OrderContractsInternal.RemoveByHolder(o);
        return Observable.Empty<ErrorMessages<OrderContractHolder>>();
      } else {
        var oso = OrderStatusObservable.Where(m => m.IsOrderDone());
        var wte = IbClient.WireToErrorMessage(orderId, oso, m => m.OrderId
        , m => OrderContractsInternal.Items.ByOrderId(m.OrderId)
        , Default
        , e => {
          if(e.errorCode == ORDER_TOCANCEL_NOTFOUND)
            OrderContractsInternal.RemoveByOrderId(orderId);
          return true.SideEffect(_ => Trace($"CancelOrder Error: {e}"));
        });
        IbClient.OnReqMktData(() => IbClient.ClientSocket.cancelOrder(orderId));
        return wte.FirstAsync();
      }
      IEnumerable<OrderContractHolder> Default() { yield break; }
    }
    public IObservable<OrderContractHolderWithError[]> OpenEdgeOrder(string instrument, bool isCall, int quantity, int daysToSkip, double edge, double takeProfitPoints, string ocaGroup = "")
      => OpenEdgeOrder(null, instrument, isCall, quantity, daysToSkip, edge, takeProfitPoints, ocaGroup);
    public IObservable<OrderContractHolderWithError[]> OpenEdgeOrder(Action<IBApi.Order> changeOrder, string instrument, bool isCall, int quantity, int daysToSkip, double edge, double takeProfitPoints, string ocaGroup = "") {
      var @params = OpenEdgeOrderParams(instrument, isCall, quantity, daysToSkip, edge, takeProfitPoints);
      return OpenEdgeOrder(changeOrder, @params, ocaGroup);
    }
    public IObservable<OrderContractHolderWithError[]> OpenEdgeOrder(Action<IBApi.Order> changeOrder, IObservable<OpenEdgeParams> @params, string ocaGroup = "") {
      var update = from p in @params.Take(0)
                   from orderToUpdate in CanUpdateOrder(p.contract, p.quantity, OrderTypes.LMT, p.enterConditions)
                   where orderToUpdate.context.type && orderToUpdate.context.condition
                   from uo in UpdateEdgeOrder(orderToUpdate.och, p.price, p.enterConditions)
                   select uo;
      var open = from p in @params
                 from ps in p.currentOrders.Where(co => !co.me).Select(currentOrder => CancelOrder(currentOrder.holder.order.OrderId)).Merge().ToArray()
                 let canceled = ps.Any(p => !p.error.HasError ? false : throw GenericException.Create(new { context = p.value.Flatter(","), p.error }))
                 from ots in OpenTrade(o => {
                   changeOrder?.Invoke(o);
                   var limitOrder = MakeTakeProfitOrder2(o, p.contract, p.takeProfit);
                   if(!ocaGroup.IsNullOrEmpty()) {
                     o.OcaGroup = ocaGroup;
                     o.OcaType = (int)OCAType.CancelWithBlocking.Value;
                   }
                   return limitOrder ?? Observable.Empty<OrderPriceContract>();
                 }, p.contract, p.quantity, condition: p.enterConditions.SingleOrDefault(), useTakeProfit: false, price: p.price)
                 select ots;
      return update.Concat(open).Where(a => a.Any()).Take(1);
      IObservable<OrderContractHolderWithError[]> UpdateEdgeOrder(OrderContractHolder och, double price, OrderCondition[] enterConditions) {
        och.order.LmtPrice = price;
        och.order.VolatilityType = 0;
        och.order.Conditions.OfType<PriceCondition>()
          .Zip(enterConditions.OfType<PriceCondition>(), (pc, ec) => pc.Price = ec.Price).Count();
        changeOrder(och.order);
        return PlaceOrder(och.order, och.contract).Select(em => new OrderContractHolderWithError(em)).ToArray();
      }
    }
    public IObservable<OpenEdgeParams>
      OpenEdgeOrderParams(string instrument, bool isCall, int quantity, int daysToSkip, double edge, double takeProfitPoints) {
      var mul = isCall ? -1 : 1;
      var enterLevel = edge;
      var exitLevel = enterLevel + takeProfitPoints * mul;
      return
      from ucd in instrument.ReqContractDetailsCached()
      let underContract = ucd.Contract
      from underPrice in underContract.ReqPriceSafe()
      let enterCondition = underContract.PriceCondition(enterLevel.ThrowIf(() => isCall ? enterLevel < underPrice.ask : enterLevel > underPrice.bid), isCall)
      //let exitCondition = underContract.PriceCondition(exitLevel, !isCall)
      let cp = underPrice.avg
    .ThrowIf(() => isCall && underPrice.avg > enterLevel)
    .ThrowIf(() => !isCall && underPrice.avg < enterLevel)
      from current in CurrentOptions(instrument, cp, daysToSkip, 2, c => c.IsCall == isCall)
      from combo in CurrentOptionOutMoney(instrument, enterLevel, isCall, daysToSkip)
      let contract = combo.option
      let price = current.Average(c => c.option.ExtrinsicValue(c.marketPrice.bid, underPrice.bid))
      let takeProfit = price.Min(price / 2, takeProfitPoints * 0.5)
      let currentOrders = FindEdgeOrders(contract)
      select new OpenEdgeParams(
        contract,
        quantity,
        new[] { enterCondition },
        price,
        takeProfit,
        currentOrders
        );

    }
    public IEnumerable<(OrderContractHolder holder, bool me)> FindEdgeOrders(Contract contract) {
      var edgeOrders = UseOrderContracts(ocs => ocs.Where(oc =>
        oc.order.IsSell &&
        !oc.isDone &&
        oc.contract.IsOption &&
        oc.contract.IsCall == contract.IsCall &&
        oc.contract.Symbol == contract.Symbol &&
        oc.order.HasPriceCodition
      )).Concat();
      return edgeOrders.Select(h => (h, contract.Key == h.contract.Key));
      //edgeOrders.ThrowIf(() => edgeOrders.IsEmpty());

    }

    IEnumerable<(OrderContractHolder och, (bool type, bool condition) context)> CanUpdateOrder(Contract contract, int quantity, OrderTypes orderType, IList<OrderCondition> conditions) {
      var eos = FindExistingOrder(contract, quantity, och => {
        var type = EnumUtils.Compare(och.order.OrderType, orderType, true);
        var condition = och.order.Conditions.OfType<PriceCondition>().Select(parseCond)
        .SequenceEqual(conditions.OfType<PriceCondition>().Select(parseCond));
        return (type, condition);
      });
      return eos;
      (bool IsConjunctionConnection, bool IsMore) parseCond(PriceCondition pc) => (pc.IsConjunctionConnection, pc.IsMore);
    }


    IObservable<CurrentCombo> CurrentOptionOutMoney(string instrument, double enterLevel, bool isCall, int daysToSkip) {
      var mul = isCall ? -1 : 1;
      return from options in CurrentOptions(instrument, enterLevel, daysToSkip, 2, c => c.IsCall == isCall)
             from option in options.OrderBy(o => o.option.Strike * mul).Take(1)
             select option;
    }
    double OrderPrice(double orderPrice, Contract contract) {
      var minTick = contract.MinTick();
      var p = (Math.Round(orderPrice / minTick) * minTick);
      p = Math.Round(p, 4);
      return p;
    }

    public void CancelAllOrders(string message) {
      Trace($"{nameof(CancelAllOrders)}: {message}");
      IbClient.OnReqMktData(() => IbClient.ClientSocket.reqGlobalCancel());
      //UseOrderContracts(ocs => ocs.Values.Where(oc => oc.isNew).ForEach(_ => ocs.TryRemove(_.order.OrderId, out var __)));
    }
    public static void FillAdaptiveParams(IBApi.Order baseOrder, string priority) {
      baseOrder.AlgoStrategy = "Adaptive";
      baseOrder.AlgoParams = new List<TagValue>();
      baseOrder.AlgoParams.Add(new TagValue("adaptivePriority", priority));
    }

    public void OpenLimitOrder(Contract contract, int quantity, double profit, bool useMarketPrice, bool useTakeProfit) {
      double ask(MarketPrice p) => useMarketPrice ? p.ask : p.bid;
      double bid(double a, double b) => useMarketPrice ? b : a;
      contract.ReqPriceSafe().Select(p => quantity > 0 ? ask(p) : bid(p.ask, p.bid))
       .Subscribe(price => OpenTrade(contract, quantity, price, profit, useTakeProfit).Subscribe());
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
       from p in needLimitPrice ? c.ReqPriceSafe(2) : IbClient.MarketDataManager.ReqPriceEmpty()
       from up in conditionPrice.HasValue ? new[] { conditionPrice.Value }.ToObservable() : uc.ReqPriceSafe().Select(p => p.ask.Avg(p.bid))
       let condProfit = uc.PriceCondition(up + upProfit, isMore, false)
       let price = isSell ? p.bid : p.ask
       from po in OpenTrade(c, quantity, hasCondition ? 0 : price, 0, true, DateTime.MaxValue, dateAfter
       , hasCondition ? uc.PriceCondition(conditionPrice.Value, isSell && c.IsCall, false) : null, condProfit, "")
       select po
       )
       .Subscribe();
    }

    public IObservable<OrderContractHolderWithError[]> OpenTrade
      (Contract contract, int quantity, double price = 0, double profit = 0, bool useTakeProfit = false
      , DateTime goodTillDate = default, DateTime goodAfterDate = default
      , OrderCondition condition = null, OrderCondition takeProfitCondition = null
      , [CallerMemberName] string Caller = "") => OpenTrade((ORDEREXT)null, contract, quantity, price, profit, useTakeProfit, goodTillDate, goodAfterDate, condition, new[] { takeProfitCondition }, "");

    public IObservable<OrderContractHolderWithError[]> OpenTradeWithAction
      (
      Action<IBApi.Order> orderExt
      , Contract contract, int quantity, double price = 0, double profit = 0, bool useTakeProfit = false
      , DateTime goodTillDate = default, DateTime goodAfterDate = default
      , OrderCondition condition = null, IList<OrderCondition> takeProfitCondition = null
      , string orderRef = ""
      , [CallerMemberName] string Caller = "")
      => OpenTrade(o => {
        orderExt(o);
        return null;
      }
      , contract, quantity, price, profit, useTakeProfit
        , goodTillDate, goodAfterDate
        , condition, takeProfitCondition, orderRef, Caller);

    public IObservable<OrderContractHolderWithError[]> OpenTrade
    (
      ORDEREXT orderExt
    , Contract contract, int quantity, double price = 0, double profit = 0, bool useTakeProfit = false
    , DateTime goodTillDate = default, DateTime goodAfterDate = default
    , OrderCondition condition = null, IList<OrderCondition> takeProfitCondition = null
    , string orderRef = ""
    , [CallerMemberName] string Caller = "") {
      contract.Check();
      string type = "";
      Verbose($"{nameof(OpenTrade)}[S]: {new { contract, quantity, orderRef }} <= {Caller}");
      if(useTakeProfit && profit == 0 && takeProfitCondition?.Count == 0)
        return Default(new Exception($"No profit or profit condition: {new { useTakeProfit, profit, takeProfitCondition }}")).Do(TraceOpenTradeResults).ToArray();
      var aos = FindExistingOrder(contract, quantity).ToArray();
      var subs = aos.Where(oc => oc.isSubmitted).ToList();
      if(subs.Any())
        return subs.Select(s => (OrderContractHolderWithError)(s, new ErrorMessage(0, -1, $"Order {s} is already active", null))).ToObservable().ToArray();

      OrderContractHolder TraceLocal(OrderContractHolder ao) { Trace($"OpenTrade: Cancelling existing order {ao.order}[{ao.order.OrderId}] for { contract } with {new { ao.status.status }}."); return ao; }
      if(aos.Any()) {
        var cs = (from ao in aos.ToObservable()
                  from c in CancelOrder(TraceLocal(ao).order.OrderId)
                  select c
                  ).ToArray();
        return (from c in cs
                from a in action()
                select a);
      }
      return action();

      IObservable<OrderContractHolderWithError[]> action() {
        try {
          var orderType = price == 0
            ? /*(contract.IsFuturesCombo ? "REL + " : "") + */"MKT"
            : type.IfEmpty(contract.IsHedgeCombo ? "LMT"/* + MKT"*/ : "LMT");
          bool isPreRTH = true;// contract.IsPreRTH(IBClientMaster.ServerTime);
          var order = OrderFactory(contract, quantity, price, goodTillDate, goodAfterDate, orderType, isPreRTH);
          order.OrderRef = orderRef;
          order.Conditions.AddRange(condition.YieldNotNull());
          order.ConditionsIgnoreRth = true;
          var child = orderExt?.Invoke(order) ?? Observable.Empty<OrderPriceContract>();
          CheckNonGiuaranteed(order, contract);

          if(order.NeedTriggerPrice && order.LmtPrice.IsSetAndNotZero() && order.AuxPrice.IsNotSetOrZero()) order.AuxPrice = order.LmtPrice;
          if(false && !contract.IsOptionsCombo && !contract.IsFutureOption) FillAdaptiveParams(order, "Normal");
          var tpOrder = (useTakeProfit
            ? MakeTakeProfitOrder2(order, contract, takeProfitCondition, profit)
            : Observable.Empty<OrderPriceContract>()
            );
          var orders = new[] { new OrderPriceContract(order, price, contract) }
          .ToObservable()
          .Merge(tpOrder)
          .Merge(child)
          .ToArray();
          var obss = (from os in orders
                      from o in os.OrderBy(o => o.order.ParentId)
                      from po in PlaceOrder(o.order, o.contract).Take(1)//.Spy($"PlaceTrade[{o.order.OrderId}]", t => TraceDebug(t))
                      from value in po.value.DefaultIfEmpty()//.SideEffect(v => TraceDebug($"PlacedTrade[{po.value.Single().order.OrderId}]"))
                      select (OrderContractHolderWithError)(value, po.error));
          return obss.Do(TraceOpenTradeResults).ToArray();
        } catch(Exception exc) {
          return Default(exc).ToArray();
        }
      }
      IObservable<OrderContractHolderWithError> Default(Exception exc) =>
        Observable.Return((OrderContractHolderWithError)(default(OrderContractHolder), new ErrorMessage(0, 0, nameof(OpenTrade), exc)));
    }

    private IEnumerable<OrderContractHolder> FindExistingOrder(Contract contract, int quantity) => FindExistingOrder(contract, quantity, _ => false).Select(t => t.och);
    private IEnumerable<(OrderContractHolder och, T context)> FindExistingOrder<T>(Contract contract, int quantity, Func<OrderContractHolder, T> foo) =>
      UseOrderContracts(ocs => ocs
      .Where(oc => !oc.isDone && oc.contract == contract && oc.order.TotalPosition().Sign() == quantity.Sign())
      .Select(och => (och, foo(och))))
      .Concat();

    private static IBApi.Order CheckNonGiuaranteed(IBApi.Order order, Contract contract) {
      if(contract.IsBag && !contract.HasFutureOption || order.NeedTriggerPrice) {
        order.SmartComboRoutingParams = new List<TagValue>();
        order.SmartComboRoutingParams.Add(new TagValue("NonGuaranteed", "1"));
        //order.Transmit = false;
      }
      if(order.NeedTriggerPrice && order.LmtPrice.IsSetAndNotZero())
        order.AuxPrice = order.LmtPrice;
      return order;
    }

    private void TraceOpenTradeResults(OrderContractHolderWithError a) => Verbose(
                new[] { a.value.Select(_ => new { reqId = _?.order?.OrderId ?? a.error.reqId, order = _.order, a.error }).ToTextOrTable(nameof(OpenTrade) + "[E]:") + "" }
                .Concat(new[] { OrderContractsInternal.Items.Select(x => new { x.order.OrderId, x.order, x.contract, x.status }).ToTextOrTable(nameof(OrderContractsInternal) + ":") + "" })
                .Flatter("\n"));

    private IBApi.Order OrderFactory(Contract contract, double quantity, double price, DateTime goodTillDate, DateTime goodAfterDate, string orderType, bool isPreRTH) {
      var order = new IBApi.Order() {
        Account = _accountId,
        OrderId = NetOrderId(),
        Action = quantity > 0 ? "BUY" : "SELL",
        OrderType = orderType,
        TotalQuantity = quantity.Abs(),
        OutsideRth = isPreRTH,
        OverridePercentageConstraints = true,
        Transmit = true
      };
      order.SetLimit(OrderPrice(price, contract));
      if(goodTillDate != default && !goodTillDate.IsMax()) {
        order.Tif = GTD;
        order.GoodTillDate = goodTillDate.ToTWSString();
      } else
        order.Tif = GTC;
      if(goodAfterDate != default)
        order.GoodAfterTime = goodAfterDate.ToTWSString();
      CheckNonGiuaranteed(order, contract);
      return order;
    }

    public IObservable<OrderPriceContract> MakeTakeProfitOrder2(IBApi.Order parent, Contract contract, double profit = double.NaN, double limitPrice = double.NaN) {
      Passager.ThrowIf(() => profit.IsSetAndNotZero() && limitPrice.IsSetAndNotZero());
      Passager.ThrowIf(() => profit.IsSetAndNotZero() && parent.IsMarket);
      var transmit = parent.Transmit;
      parent.Transmit = false;
      var takeProfit = parent.LmtPrice + (profit >= 1 ? profit : parent.LmtPrice * profit) * (parent.IsBuy ? 1 : -1);
      var price = limitPrice.IfNaNOrZero(takeProfit);
      var orderType = "LMT" + (contract.IsHedgeCombo ? " + MKT" : "");
      var order = OrderFactory(contract, -parent.Quantity, price, default, default, orderType, parent.OutsideRth);
      order.ParentId = parent.OrderId;
      order.Transmit = transmit;
      return Observable.Return((OrderPriceContract)(order, price, contract));
    }
    public IObservable<OrderPriceContract> MakeTakeProfitOrder2(IBApi.Order parent, Contract contract, IList<OrderCondition> takeProfitCondition) {
      var takeProfitPrice = takeProfitCondition.OfType<PriceCondition>().ToList();
      var transmit = parent.Transmit;
      parent.Transmit = false;
      var orderType = "MKT";
      var order = OrderFactory(contract, -parent.Quantity, 0, default, default, orderType, parent.OutsideRth);
      order.ParentId = parent.OrderId;
      order.Transmit = transmit;
      order.Conditions.AddRange(takeProfitCondition.Where(c => c != null));
      order.ConditionsIgnoreRth = true;
      return Observable.Return((OrderPriceContract)(order, 0.0, contract));
    }

    public IObservable<OrderPriceContract> MakeTakeProfitOrder2(IBApi.Order parent, Contract contract, IList<OrderCondition> takeProfitCondition, double profit) {
      if(takeProfitCondition.IsEmpty() && profit.IsNaNOrZero()) {
        TraceError("Either takeProfitCondition or profit must be present");
        throw new Exception("Either takeProfitCondition or profit must be present");
      }
      var takeProfitPrice = takeProfitCondition.OfType<PriceCondition>().ToList();
      var isMarket = takeProfitPrice.Any();
      var transmit = parent.Transmit;
      return new[] { parent }
      .Select(o => o.LmtPrice)
      .ToObservable()
      .Concat(Observable.Defer(()
      => contract.ReqPriceSafe(2).Select(p => parent.IsBuy ? p.ask : p.bid).SubscribeOn(Scheduler.CurrentThread)
      ).SubscribeOn(Scheduler.CurrentThread)
      ).SubscribeOn(Scheduler.CurrentThread)
      //.OnEmpty(() => Trace($"No take profit order for {parent}"))
      .Select(lmtPrice => {
        parent.Transmit = false;
        var takeProfit = (profit >= 1 ? profit : lmtPrice * profit) * (parent.IsBuy ? 1 : -1);
        var price = lmtPrice + takeProfit;
        var orderType = isMarket ? "MKT" : "LMT" + (contract.IsHedgeCombo ? " + MKT" : "");
        var order = OrderFactory(contract, -parent.Quantity, price, default, default, orderType, parent.OutsideRth);
        order.ParentId = parent.OrderId;
        order.Transmit = transmit;
        order.Conditions.AddRange(takeProfitCondition.Where(c => c != null));
        order.ConditionsIgnoreRth = true;
        return (OrderPriceContract)(order, price, contract);
      })
      .Take(1)
      .OnEmpty(() => Trace($"No take profit order for {parent}"))
        ;
    }

    public IObservable<OrderContractHolderWithError[]> OpenRollTrade(string currentSymbol, string rollSymbol, bool isTest) {
      return (from cc in IbClient.ReqContractDetailsCached(currentSymbol).Select(cd => cd.Contract)
              from rc in IbClient.ReqContractDetailsCached(rollSymbol).Select(cd => cd.Contract)
              from ct in ComboTrades(5)
              where ct.contract.ConId == cc.ConId
              select (cc, rc, ct)
       )
       .SelectMany(t => {
         var tradeDateCondition = IbClient.ServerTime.Date.AddHours(15).AddMinutes(45).TimeCondition();
         if(t.cc.IsOption)
           return CreateRoll(currentSymbol, rollSymbol)
             .SelectMany(rc => OpenTradeWithAction(
               orderExt: o => o.Transmit = !isTest,
               contract: rc.rollContract,
               quantity: -rc.currentTrade.position));
         else
           return OpenTradeWithAction(
             orderExt: o => o.Transmit = !isTest,
             contract: t.rc,
             quantity: -t.ct.position.Abs());
       });
    }
    public IObservable<OrderContractHolderWithError[]> OpenHedgeOrder(Contract parentContract, Contract hedgeContract, int quantityParent, int quantityHedge) {
      Func<IBApi.Order, IObservable<OrderPriceContract>> orderExt(Contract c, int q) =>
        parent => Observable.Return((OrderPriceContract)(MakeOCOOrder(parent, q), 0.0, c));
      var och = (
        from c in parentContract.ReqContractDetailsCached().Select(cd => cd.Contract)
        from c2 in hedgeContract.ReqContractDetailsCached().Select(cd => cd.Contract)
        from p in c.ReqPriceSafe().Select(ab => quantityParent > 0 ? ab.ask : ab.bid)
        from ot in OpenTrade(orderExt(c2, quantityHedge), c, quantityParent, p)
        select ot
       );
      return och;
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

  //  public struct OrderContractHolderWithError {
  //    public AccountManager.OrderContractHolder holder;
  //    public ErrorMessage error;

  //    public OrderContractHolderWithError(AccountManager.OrderContractHolder holder, ErrorMessage error) {
  //      this.holder = holder;
  //      this.error = error;
  //    }

  //    public override bool Equals(object obj) => obj is OrderContractHolderWithError other && EqualityComparer<AccountManager.OrderContractHolder>.Default.Equals(holder, other.holder) && EqualityComparer<ErrorMessage>.Default.Equals(error, other.error);

  //    public override int GetHashCode() {
  //      var hashCode = -1913285320;
  //      hashCode = hashCode * -1521134295 + EqualityComparer<AccountManager.OrderContractHolder>.Default.GetHashCode(holder);
  //      hashCode = hashCode * -1521134295 + error.GetHashCode();
  //      return hashCode;
  //    }

  //    public void Deconstruct(out AccountManager.OrderContractHolder holder, out ErrorMessage error) {
  //      holder = this.holder;
  //      error = this.error;
  //    }

  //    public static implicit operator (AccountManager.OrderContractHolder holder, ErrorMessage error)(OrderContractHolderWithError value) => (value.holder, value.error);
  //    public static implicit operator OrderContractHolderWithError((AccountManager.OrderContractHolder holder, ErrorMessage error) value) => new OrderContractHolderWithError(value.holder, value.error);
  //  }
  public struct OrderPriceContract {
    public IBApi.Order order;
    public double price;
    public Contract contract;

    public OrderPriceContract(IBApi.Order order, double price, Contract contract) {
      this.order = order;
      this.price = price;
      this.contract = contract;
    }

    public override bool Equals(object obj) => obj is OrderPriceContract other && EqualityComparer<IBApi.Order>.Default.Equals(order, other.order) && price == other.price && EqualityComparer<Contract>.Default.Equals(contract, other.contract);

    public override int GetHashCode() {
      var hashCode = 1695776068;
      hashCode = hashCode * -1521134295 + EqualityComparer<IBApi.Order>.Default.GetHashCode(order);
      hashCode = hashCode * -1521134295 + price.GetHashCode();
      hashCode = hashCode * -1521134295 + EqualityComparer<Contract>.Default.GetHashCode(contract);
      return hashCode;
    }

    public void Deconstruct(out IBApi.Order order, out double price, out Contract contract) {
      order = this.order;
      price = this.price;
      contract = this.contract;
    }

    public static implicit operator (IBApi.Order order, double price, Contract contract)(OrderPriceContract value) => (value.order, value.price, value.contract);
    public static implicit operator OrderPriceContract((IBApi.Order order, double price, Contract contract) value) => new OrderPriceContract(value.order, value.price, value.contract);
  }
}

