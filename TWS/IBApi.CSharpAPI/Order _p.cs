using HedgeHog;
using System;

namespace IBApi {
  partial class Order {
    public bool IsSell => Action == "SELL";
    public bool IsBuy => Action == "BUY";

    public bool IsLimit => OrderType.Contains("LMT");
    public bool NeedTriggerPrice => OrderType.Contains("+");
    public void SetLimit(double price) {
      LmtPrice = price;
      if(NeedTriggerPrice)
        AuxPrice = price;
    }
    public string TypeText => $"{OrderType}{(IsLimit ? "[" + LmtPrice + "]" : "")}";
    public string ActionText => Action.Substring(0,3);
    public string Key => $"{ActionText}:{TypeText}:{TotalQuantity}";
    public double LmtAuxPrice => LmtPrice.IfNotSetOrZero(AuxPrice.IfNotSetOrZero(0));
    public override string ToString() => $"{Key}{Conditions.ToText(":")}";
    public static double OrderPrice(double orderPrice, Contract contract) {
      var minTick = contract.MinTick();
      var p = (Math.Round(orderPrice / minTick) * minTick);
      p = Math.Round(p, 4);
      return p;
    }

  }
}
