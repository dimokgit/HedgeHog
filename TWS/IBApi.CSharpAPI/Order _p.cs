namespace IBApi {
  partial class Order {
    public bool IsLimit => OrderType == "LMT";
    public override string ToString() => $"{Action}:{OrderType}:{TotalQuantity}";
  }
}
