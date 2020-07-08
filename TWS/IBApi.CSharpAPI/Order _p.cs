using HedgeHog;
using System;

namespace IBApi {
  partial class Order {
    [Flags]
    public enum OrderTypes { MKT, LMT, MIDPRICE, Adaptive };
    public bool IsSell => Action == "SELL";
    public bool IsBuy => Action == "BUY";
    public string SetAction(double quanity) => Action = (quanity > 0 ? "BUY" : "SELL");
    public void SetGoodAfter(DateTime d) { if(d != default) GoodAfterTime = d.ToTWSString(); }
    public bool IsLimit => OrderType.Contains("LMT");
    public bool NeedTriggerPrice => OrderType.Contains("+");
    public void SetLimit(double price) {
      LmtPrice = price;
      if(NeedTriggerPrice)
        AuxPrice = price;
    }
    public string TypeText => $"{OrderType}{(IsLimit ? "[" + LmtPrice + "]" : "")}";
    public string ActionText => Action.Substring(0, 3);
    public string Key => $"{ActionText}:{TypeText}:{TotalQuantity}";
    public double LmtAuxPrice => LmtPrice.IfNotSetOrZero(AuxPrice.IfNotSetOrZero(0));
    public override string ToString() => $"{Key}{Conditions.ToText(":")}";
    public static double OrderPrice(double orderPrice, Contract contract) {
      var minTick = contract.MinTick();
      var p = (Math.Round(orderPrice / minTick) * minTick);
      p = Math.Round(p, 4);
      return p;
    }
    public Order SetDefaultType(Contract contract, DateTime serverTime) {
      if(!OrderType.IsNullOrEmpty()) {
        if(LmtPrice.IsNotSetOrZero())
          OrderType = OrderTypes.LMT + "";
        if(contract.IsStock)
          SetType(contract, serverTime, OrderTypes.MIDPRICE);
        else if(contract.IsFuture)
          SetType(contract, serverTime, OrderTypes.Adaptive);
        else OrderType = OrderTypes.MKT + "";
      }
      return this;
    }
    public Order SetType(Contract contract, DateTime serverTime, IBApi.Order.OrderTypes type) {
      switch(type) {
        case OrderTypes.MIDPRICE:
          if(contract.IsStock) {
            OutsideRth = !serverTime.Between(contract.LiquidHours);
            OrderType = (OutsideRth ? IBApi.Order.OrderTypes.MKT : type) + "";
          }
          break;
        case OrderTypes.Adaptive:
          if(contract.IsStock || contract.IsFuture) {
            AlgoStrategy = type + "";
            AlgoParams = new System.Collections.Generic.List<TagValue> { new TagValue("adaptivePriority", "Normal") };
          }
          break;
        default:
          OrderType = type + "";
          break;
      }
      return this;
    }

  }
}
