using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Alice.Store;
using IBApi;
using IBApp;
using Westwind.Web.WebApi;

namespace HedgeHog.Alice.Client {
  partial class MyHub {
    static bool _isTest = false;
    #region OpenButterfly
    [BasicAuthenticationFilter]
    public async Task<string[]> OpenButterfly(string pair, string instrument, int quantity, bool useMarketPrice, double? conditionPrice, double? profitInPoints, string rollTrade = null, bool isTest = false) {
      if(!rollTrade.IsNullOrWhiteSpace()) {
        return await RollTrade(rollTrade, instrument, isTest);
      }

      var tm = UseTraderMacro(pair).Single();
      var std = tm.StraddleRangeM1().TakeProfit;// tm.RatesArraySafe.StandardDeviation(r => r.PriceAvg);
      var profit = profitInPoints.GetValueOrDefault(std);
      if(profit == 0) throw new Exception("No trend line found for profit calculation");
      var am = ((IBWraper)trader.Value.TradesManager).AccountManager;
      if(IBApi.Contract.Contracts.TryGetValue(instrument, out var contract))
        return await OpenCondOrder(am, tm, contract, null, quantity, (conditionPrice.GetValueOrDefault(), pair), profit, isTest);
      else
        return new[] { new { instrument, not = "found" } + "" };
    }
    static async Task<string[]> OpenCondOrder(AccountManager am, TradingMacro tm, Contract option, bool? isCall, int quantity, (double price, string instrument) condition, double profit, bool isTest) {
      if(option != null && isCall != null || option == null && isCall == null)
        throw new Exception(new { OpenCondOrder = new { option, isCall } } + "");
      {
        var std = tm.RatesArraySafe.StandardDeviation(r => r.PriceAvg);
        var hasStrategy = tm.Strategy.HasFlag(Strategies.Universal);
        var bs = hasStrategy ? new { b = tm.BuyLevel.Rate, s = tm.SellLevel.Rate } : new { b = double.NaN, s = double.NaN };
        if(hasStrategy && (bs.s.IsNaN() || bs.b.IsNaN()))
          throw new Exception("Buy/Sell levels are not set by strategy");
        var isSell = quantity < 0;
        var isBuy = quantity > 0;
        var hasCondition = condition.price > 0;
        int Delta(Contract c) => c.DeltaSign * quantity.Sign();
        Action<IBApi.Order> orderExt = o => o.Transmit = !isTest;
        var res = await (from contract in Observable.Return(option)
                         from price in contract.ReqPriceSafe()
                         from underContract in contract.UnderContract
                         from underPrice in underContract.ReqPriceSafe().Select(p => p.ask.Avg(p.bid))
                         let condPrice = condition.price.IfNaNOrZero(hasStrategy
                         ? contract.IsPut && isBuy || contract.IsCall && isSell
                         ? bs.s : bs.b : 0).Round(2)
                         let isMoreOrder = condPrice.IsNaNOrZero() ? (bool?)null : condPrice > underPrice
                         let upProfit = profit * Delta(contract) * price.delta
                         let condTakeProfit = contract.IsCallPut
                         ? buildConditions(underContract
                          , condition.price.IfNaNOrZero(underPrice).With(p
                          => contract.ComboStrike().With(s
                          => new[] { s - std / 4, s + std / 4 })), isBuy, tm.ServerTime.AddHours(2), a => orderExt += a)
                         : new[] { underContract.PriceCondition((condPrice.IfNaNOrZero(underPrice) + upProfit).Round(2), upProfit > 0, false) }
                         let t = new { price = condPrice == 0 ? limitPrice(contract, isSell, price) : 0 }
                         from ots in am.OpenTradeWithAction(orderExt, contract, quantity, t.price, 0
                          , (bool)condTakeProfit?.Any(), DateTime.MaxValue, default
                          , t.price != 0 || !isMoreOrder.HasValue ? null
                          : underContract.PriceCondition(condPrice.Round(2), isMoreOrder.Value, false)
                          , condTakeProfit)
                         from ot in ots
                         where ot.error.HasError
                         select ot.error.exc?.Message ?? $"{ot.error.errorCode}: {ot.error.errorMsg}"
          ).ToArray();
        return res;
      }
      double limitPrice(Contract c, bool isSell, MarketPrice p) {
        if(isSell) return c.IsCallPut ? p.bid : p.bid;
        return c.IsCallPut ? p.ask : p.ask;
      }
      IList<OrderCondition> buildConditions
        (Contract condContract, IList<double> prices, bool isMore, DateTime goodAfter = default, Action<Action<IBApi.Order>> orderExt = default) {
        if(prices.Count == 1)
          return prices.Select(p => condContract.PriceCondition(p, isMore)).ToList();
        if(goodAfter == default) throw new Exception($"{nameof(goodAfter)} parameter is missing, {new { goodAfter }}");
        if(prices.Count != 2)
          throw new Exception($"{nameof(prices)} parameter must have exactly 2 prices, {new { prices = prices.Flatter(",") }}");
        // Set between condition
        return prices.OrderBy(p => p).Select((p, i) => condContract.PriceCondition(p, isMore ? i != 0 : i == 0, !isMore))
          .Concat(new[] { goodAfter.TimeCondition(true, true) }.Where(_ => !isMore))
          .ToList();
      }
    }
    #endregion

    #region CreateEdgeOrder
    public enum EdgeRangeType { T, S }
    [BasicAuthenticationFilter]
    public Task OpenEdgeOrder(string pair, bool isCall, int quantity, int daysToSkip, double currentStrikeLevel, double profitInPoints, EdgeRangeType rangeType, bool isTest)
      => OpenTrendOrder(pair, isCall, quantity, daysToSkip, currentStrikeLevel, profitInPoints, rangeType==EdgeRangeType.S?StraddleEdges:(EdgesDelegate)TrendEdges , isTest);

    
    private async Task OpenTrendOrder(string pair, bool isCall, int quantity, int daysToSkip, double currentStrikeLevel, double profitInPoints, EdgesDelegate calcEdges, bool isTest) {
      var am = GetAccountManager();
      var range = calcEdges(pair, isCall).First();
      var edge = currentStrikeLevel.IfNotSetOrZero(range.edge);
      if(edge.IsNotSetOrZero()) throw new Exception($"No {nameof(TrendEdges)} edges found");
      var takeProfitPoints = profitInPoints.IfNotSetOrZero(range.profit);// tm.RatesArraySafe.StandardDeviation(r => r.PriceAvg);
      {
        var x = await (from ots in am.OpenEdgeOrder(o => o.Transmit = !isTest, pair, isCall, -quantity, daysToSkip, edge, takeProfitPoints, "oca-edge-options:" + DateTime.Now.Ticks)
                       from ot in ots
                       where ot.error.HasError
                       select ot.error.ToString()
                      ).DefaultIfEmpty();
        if(!x.IsNullOrEmpty()) throw new Exception(x);
      }
    }
    private IEnumerable<(double edge, double profit)> StraddleEdges(string pair, bool isCall) =>
      UseTradingMacro(pair, tm => tm.IsTrader, tm =>
        tm.StraddleRangeM1().With(r => (isCall ? r.Up : r.Down, r.TakeProfit))
      );
    private IEnumerable<(double edge, double profit)> TrendEdges(string pair, bool isCall) =>
      UseTradingMacro(pair, tm => tm.IsTrader, tm => tm.TrendEdge(isCall));
    delegate IEnumerable<(double edge, double profit)> EdgesDelegate(string pair, bool isCall);

      #endregion
    }
  }
