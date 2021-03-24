using HedgeHog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IBApi {
  partial class Order {
    [Flags]
    public enum OrderTypes { MKT, LMT, MIDPRICE, Adaptive };
    public bool IsSell => Action == "SELL";
    public bool IsBuy => Action == "BUY";
    public string SetAction(double quanity) => Action = (quanity > 0 ? "BUY" : "SELL");
    public void SetGoodAfter(DateTime d) { if(d != default) GoodAfterTime = d.ToTWSString(); }
    public bool IsLimit => OrderType.Contains("LMT");
    public bool IsMarket => OrderType.Contains("MKT");
    public bool NeedTriggerPrice => OrderType.Contains("+");
    public void SetLimit(double price, string orderType) {
      OrderType = orderType;
      SetLimit(price);
    }
    public void SetLimit(double price, bool force = false) {
      if(price != 0 && OrderType.IsNullOrEmpty()) throw new Exception("SetLimit with price != 0  can not be used when OrderType is empty.");
      if(IsLimit || !IsMarket || NeedTriggerPrice || force)
        LmtPrice = price;
      if(LmtPrice.IsSetAndNotZero())
        OrderType = OrderTypes.LMT.ToString();
      if(NeedTriggerPrice)
        AuxPrice = price;
    }
    public double Quantity => IsBuy ? TotalQuantity : -TotalQuantity;
    public string TypeText => $"{OrderType}{(IsLimit ? "[" + LmtPrice + "]" : "")}";
    public string ActionText => Action.Substring(0, 3);
    public string Key => $"{ActionText}:{TypeText}:{TotalQuantity}";
    public double LmtAuxPrice => IsLimit ? LmtPrice.IfNotSetOrZero(AuxPrice.IfNotSetOrZero(0)) : 0;
    public override string ToString() => $"{Key}{Conditions.ToText(":")}";
    public static double OrderPrice(double orderPrice, Contract contract) {
      var minTick3 = contract.MinTick();
      var minTick2 = contract.MinTicks().Min();
      var mt = contract.HedgeComboPrimary((m1, m2) => throw new Exception()).SelectMany(c => c.MinTicks()).DefaultIfEmpty(contract.MinTick()).First();
      var p = Math.Round(orderPrice / mt) * mt;
      var l = (mt + "").Split('.').Skip(1).Select(s => s.Length).SingleOrDefault();
      p = Math.Round(p, l);
      return p;
    }
    public IEnumerable<PriceCondition> PriceCoditions { get { if(Conditions != null) foreach(var pc in Conditions.OfType<PriceCondition>()) yield return pc; } }
    public bool HasPriceCodition => PriceCoditions.Any();
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
