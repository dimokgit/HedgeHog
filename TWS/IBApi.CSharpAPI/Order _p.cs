namespace IBApi {
  partial class Order {
    public bool IsLimit => OrderType == "LMT";
    public string Key => $"{Action}:{OrderType}{(IsLimit ? "[" + LmtPrice + "]" : "")}:{TotalQuantity}";
    public override string ToString() => $"{Key}{Conditions.ToText(":")}";
  }
}
