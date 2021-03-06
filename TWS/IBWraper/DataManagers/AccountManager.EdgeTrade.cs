﻿using HedgeHog;
using HedgeHog.DateTimeZone;
using IBApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using static IBApi.Order;

namespace IBApp {
  public partial class AccountManager {
    public IObservable<OrderContractHolderWithError[]> OpenEdgeOrder(string instrument, bool isCall, int quantity, int daysToSkip, double edge, double takeProfitPoints, string ocaGroup = "")
      => OpenEdgeOrder(null, instrument, isCall, quantity, daysToSkip, edge, takeProfitPoints, ocaGroup);
    public IObservable<OrderContractHolderWithError[]> OpenEdgeOrder(Action<IBApi.Order> changeOrder, string instrument, bool isCall, int quantity, int daysToSkip, double edge, double takeProfitPoints, string ocaGroup = "") {
      return OpenEdgeOrderParams(instrument, isCall, daysToSkip, edge).SelectMany(p
        => OpenEdgeOrder(changeOrder, p, quantity, takeProfitPoints, ocaGroup));
    }
    public IObservable<OrderContractHolderWithError[]> OpenEdgeOrder(Action<IBApi.Order> changeOrder, OpenEdgeParams p, int quantity, double takeProfitPoints, string ocaGroup = "") {
      if(p.hasError) throw new Exception(p.levelError);
      var open =
                 from ps in p.currentOrders.Select(currentOrder => CancelOrder(currentOrder.OrderId)).Merge().ToArray()
                 let canceled = ps.Any(p => !p.error.HasError ? false : throw GenericException.Create(new { context = p.value.Flatter(","), p.error }))
                 let limitPrice = p.currentPrice - (p.currentPrice / 2).Min(takeProfitPoints * 0.5, -p.currentTheta)
                 from ots in OpenTrade(o => {
                   changeOrder?.Invoke(o);
                   var limitOrder = MakeTakeProfitOrder2(o, p.contract, limitPrice: limitPrice);
                   if(!ocaGroup.IsNullOrEmpty()) {
                     o.OcaGroup = ocaGroup;
                     o.OcaType = (int)OCAType.CancelWithBlocking.Value;
                   }
                   return limitOrder ?? Observable.Empty<OrderPriceContract>();
                 }, p.contract, quantity, condition: p.enterConditions.SingleOrDefault(), useTakeProfit: false)
                 select ots;
      return open;
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
      OpenEdgeOrderParams(string instrument, bool isCall, int daysToSkip, double edge) {
      if(IBClientMaster.ServerTime.InNewYork().Hour > 14) daysToSkip++;
      var mul = isCall ? -1 : 1;
      var enterLevel = edge;
      //var exitLevel = enterLevel + takeProfitPoints * mul;
      return
      from ucd in instrument.ReqContractDetailsCached()
      let underContract = ucd.Contract
      from underPrice in underContract.ReqPriceSafe()
      let enterCondition = underContract.PriceCondition(enterLevel, isCall)
      //let exitCondition = underContract.PriceCondition(exitLevel, !isCall)
      let levelError =
        isCall && enterLevel < underPrice.bid ? "Edge is too low " + enterLevel :
        !isCall && enterLevel > underPrice.ask ? "Edge is too high " + enterLevel : ""
      let cp = underPrice.avg
      from current in CurrentOptions(instrument, cp, daysToSkip, 2, c => c.IsCall == isCall)
      from combo in CurrentOptionOutMoney(instrument, enterLevel, isCall, daysToSkip)
      let contract = combo.option
      let currentPrice = current.Average(c => c.option.ExtrinsicValue(c.marketPrice.bid, underPrice.bid))
      let currentTheta = current.Average(c => c.marketPrice.theta)
      //let takeProfit = price.Min(price / 2, takeProfitPoints * 0.5)
      let currentOrders = FindEdgeOrders(contract)
      select new OpenEdgeParams(
        contract,
        new[] { enterCondition },
        currentPrice,
        currentTheta,
        currentOrders,
        levelError
        );

    }
    public IEnumerable<OrderContractHolder> FindEdgeOrders(IBApi.Contract contract) {
      var edgeOrders = UseOrderContracts(ocs => ocs.Where(oc =>
        oc.order.IsSell &&
        !oc.isDone &&
        oc.contract.IsOption &&
        oc.contract.IsCall == contract.IsCall &&
        oc.contract.Symbol == contract.Symbol &&
        (contract.IsCall ? oc.contract.Strike <= contract.Strike : oc.contract.Strike >= contract.Strike) &&
        oc.order.HasPriceCodition
      )).Concat();
      return edgeOrders;
      //edgeOrders.ThrowIf(() => edgeOrders.IsEmpty());

    }
  }
}
