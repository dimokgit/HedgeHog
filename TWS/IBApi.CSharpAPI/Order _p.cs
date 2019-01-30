namespace IBApi {
  partial class Order {
    public bool IsSell => Action == "SELL";
    public bool IsBuy => Action == "BUY";

    public bool IsLimit => OrderType == "LMT";
    public string TypeText => $"{OrderType}{(IsLimit ? "[" + LmtPrice + "]" : "")}";
    public string ActionText => Action.Substring(0,3);
    public string Key => $"{ActionText}:{TypeText}:{TotalQuantity}";
    public override string ToString() => $"{Key}{Conditions.ToText(":")}";
  }
}
