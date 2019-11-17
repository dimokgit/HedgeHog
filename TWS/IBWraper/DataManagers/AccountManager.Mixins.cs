using HedgeHog;
using HedgeHog.Shared;
using IBApi;
using IBSampleApp.messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using static IBApp.AccountManager;
using CURRENT_HEDGES = System.Collections.Generic.List<(IBApi.Contract contract, IBApi.Contract[] options, double ratio, double price, string context, bool isBuy)>;

namespace IBApp {
  public static class AccountManagerMixins {
    public static (Contract contract, int quantity) MakeHedgeCombo(this IEnumerable<(Contract c, double ratio)> combo, int quantity) => combo.ToList().MakeHedgeCombo(quantity);
    public static (Contract contract, int quantity) MakeHedgeCombo(this IList<(Contract c, double ratio)> combo, int quantity) =>
      combo.Count.SideEffect(c => {
        if(c != 2)
          Debugger.Break();
      }) == 2
      ? AccountManager.MakeHedgeCombo(quantity, combo[0].c, combo[1].c, combo[0].ratio, combo[1].ratio)
      : throw new Exception($"{new { combo = combo.Flatter("|") }} must have exactly two legs");
    public static (Contract buy, Contract sell, int quantity)[] CurrentOptionHedges(this CURRENT_HEDGES hh2, int maxLegQuantity, Action<string> trace = null) {
      var hh3 = hh2.SelectMany(h => getCP(h.options).Select(option => new { h.contract, isBuy = h.isBuy == option.IsCall, option, h.ratio, amount = h.price * h.ratio, h.context }));
      trace?.Invoke(hh3.ToTextOrTable("Hedge 3"));
      var hhBuy = hh3.Where(h => h.isBuy == true).Select(h => (h.option, h.ratio)).ToArray();
      trace?.Invoke(hhBuy.Select(t => new { t.option, t.ratio }).ToTextOrTable("Hedge Option Buy"));
      var hhSell = hh3.Where(h => h.isBuy == false).Select(h => (h.option, h.ratio)).ToArray();
      trace?.Invoke(hhSell.Select(t => new { t.option, t.ratio }).ToTextOrTable("Hedge Option Sell"));
      if(hhBuy.Length != 2 || hhSell.Length != 2) return new (Contract buy, Contract sell, int quantity)[0];
      var comboBuy = hhBuy.MakeHedgeCombo(maxLegQuantity);
      var comboSell = hhSell.MakeHedgeCombo(maxLegQuantity);
      if(comboBuy.quantity != comboSell.quantity) throw new Exception($"Combo buy/sell quantities should be the same:{new { comboBuy, comboSell }}");
      return new[] { (comboBuy.contract, comboSell.contract, comboBuy.quantity) };
      /// Locals
      IEnumerable<Contract> getCP(Contract[] option) => (from o in option group o by o.IsCall into g select g.First());
    }

    public static bool IsEntryOrder(this IBApi.Order o) => new[] { "MKT", "LMT" }.Contains(o.OrderType);

    public static IEnumerable<T> ByOrderId<T>(this ConcurrentDictionary<int, OrderContractHolder> source, int orderId, Func<OrderContractHolder, T> map)
      => source.ByOrderId(orderId).Select(map);
    public static IEnumerable<T> ByOrderId<T>(this IEnumerable<OrderContractHolder> source, int orderId, Func<OrderContractHolder, T> map)
      => source.ByOrderId(orderId).Select(map);
    public static IEnumerable<OrderContractHolder> ByOrderId(this ConcurrentDictionary<int, OrderContractHolder> source, int orderId) {
      if(source.TryGetValue(orderId, out var v))
        yield return v;
    }
    public static IEnumerable<OrderContractHolder> ByOrderId(this IEnumerable<OrderContractHolder> source, int orderId)
      => source.Where(och => och.order.OrderId == orderId);
    public static IEnumerable<OrderContractHolder> ByParentId(this IEnumerable<OrderContractHolder> source, int parentId)
      => source.Where(och => och.order.ParentId == parentId);

    public static IEnumerable<OrderContractHolder> ByLocalSymbol(this ConcurrentDictionary<int, OrderContractHolder> source, string localSymbol)
      => source.Where(och => och.Value.contract.LocalSymbol == localSymbol).Select(s => s.Value);
    public static IEnumerable<OrderContractHolder> ByLocalSymbool(this IEnumerable<OrderContractHolder> source, string localSymbol)
      => source.Where(och => och.contract.LocalSymbol == localSymbol);

    #region ByContract
    public static IEnumerable<OrderContractHolder> ByContract(this ConcurrentDictionary<int, OrderContractHolder> source, Contract contract) =>
      source.Values.ByContract(contract);
    public static IEnumerable<OrderContractHolder> OpenByContract(this ConcurrentDictionary<int, OrderContractHolder> source, Contract contract)
      => source.ByContract(contract).Where(och => !och.isDone && och.hasSubmitted).OrderBy(o => o.order.ParentId);
    public static IEnumerable<OrderContractHolder> ByContract(this IEnumerable<OrderContractHolder> source, Contract contract)
      => source.Where(och => och.contract == contract);
    public static IEnumerable<OrderContractHolder> OpenByContract(this IEnumerable<OrderContractHolder> source, Contract contract)
      => source.ByContract(contract).Where(och => !och.isDone && och.hasSubmitted);
    #endregion

    public static bool IsOrderDone(this OrderStatusMessage m) => (m.Status, m.Remaining).IsOrderDone();
    public static bool IsOrderDone(this (string status, double remaining) order) =>
      IBApi.OrderState.IsCancelledState(order.status) || IBApi.OrderState.IsDoneState(order.status, order.remaining);

    //public static void Verbous<T>(this T v)=>_ve

    public static bool IsSell(this IBApi.Order o) => o.Action == "SELL";
    public static bool IsBuy(this IBApi.Order o) => o.Action == "BUY";
    public static double TotalPosition(this IBApi.Order o) => o.IsBuy() ? o.TotalQuantity : -o.TotalQuantity;

    private static (string symbol, bool isBuy) Key(string symbol, bool isBuy) => (symbol.WrapPair(), isBuy);
    private static (string symbol, bool isBuy) Key2(string symbol, bool isBuy) => Key(symbol, !isBuy);

    public static (string symbol, bool isBuy) Key(this PositionMessage t) => Key(t.Contract.LocalSymbol, t.IsBuy);
    public static (string symbol, bool isBuy) Key2(this PositionMessage t) => Key2(t.Contract.LocalSymbol, t.IsBuy);

    public static (string symbol, bool isBuy) Key(this Trade t) => Key(t.Pair, t.IsBuy);
    public static (string symbol, bool isBuy) Key2(this Trade t) => Key2(t.Pair, t.IsBuy);
  }

}
