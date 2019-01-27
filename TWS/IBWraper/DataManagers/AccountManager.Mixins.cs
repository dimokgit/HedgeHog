using HedgeHog;
using HedgeHog.Shared;
using IBApi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using static IBApp.AccountManager;

namespace IBApp {
  public static class Mixins {
    public static bool IsEntryOrder(this IBApi.Order o) => new[] { "MKT", "LMT" }.Contains(o.OrderType);

    public static IEnumerable<T> ByOrderId<T>(this ConcurrentDictionary<int,OrderContractHolder> source, int orderId, Func<OrderContractHolder, T> map)
      => source.ByOrderId(orderId).Select(map);
    public static IEnumerable<T> ByOrderId<T>(this IEnumerable<OrderContractHolder> source, int orderId, Func<OrderContractHolder, T> map)
      => source.ByOrderId(orderId).Select(map);
    public static IEnumerable<OrderContractHolder> ByOrderId(this ConcurrentDictionary<int, OrderContractHolder> source, int orderId) {
      if(source.TryGetValue(orderId, out var v))
        yield return v;
    }
    public static IEnumerable<OrderContractHolder> ByOrderId(this IEnumerable<OrderContractHolder> source, int orderId)
      => source.Where(och => och.order.OrderId == orderId);

    public static IEnumerable<OrderContractHolder> ByLocalSymbool(this ConcurrentDictionary<int, OrderContractHolder> source, string localSymbol)
      => source.Where(och => och.Value.contract.LocalSymbol == localSymbol).Select(s => s.Value);
    public static IEnumerable<OrderContractHolder> ByLocalSymbool(this IEnumerable<OrderContractHolder> source, string localSymbol)
      => source.Where(och => och.contract.LocalSymbol == localSymbol);
    public static bool IsOrderDone(this (string status, double remaining) order) =>
      EnumUtils.Contains<OrderCancelStatuses>(order.status) || EnumUtils.Contains<OrderDoneStatuses>(order.status) && order.remaining == 0;

    //public static void Verbous<T>(this T v)=>_ve
    public static bool IsPreSubmited(this IBApi.OrderState order) => order.Status == "PreSubmitted";

    public static bool IsSell(this IBApi.Order o) => o.Action == "SELL";
    public static bool IsBuy(this IBApi.Order o) => o.Action == "BUY";
    public static double TotalPosition(this IBApi.Order o) => o.IsBuy() ? o.TotalQuantity : -o.TotalQuantity;

    private static (string symbol, bool isBuy) Key(string symbol, bool isBuy) => (symbol.WrapPair(), isBuy);
    private static (string symbol, bool isBuy) Key2(string symbol, bool isBuy) => Key(symbol, !isBuy);

    public static (string symbol, bool isBuy) Key(this PositionMessage t) => Key(t.Contract.LocalSymbol, t.IsBuy);
    public static (string symbol, bool isBuy) Key2(this PositionMessage t) => Key2(t.Contract.LocalSymbol, t.IsBuy);

    public static (string symbol, bool isBuy) Key(this Trade t) => Key(t.Pair, t.IsBuy);
    public static (string symbol, bool isBuy) Key2(this Trade t) => Key2(t.Pair, t.IsBuy);

    public static string Key(this Contract c) => c.Symbol + ":" + string.Join(",", c.ComboLegs?.Select(l => l.ConId)) ?? "";
  }

}
