using IBApi;
using System.Collections.Generic;

public struct ComboTradeImpl {
  public Contract contract;
  public int position;
  public double open;
  public double openPrice;
  public double takeProfit;
  public int orderId;

  public ComboTradeImpl(Contract contract, int position, double open, double openPrice, double takeProfit, int orderId) {
    this.contract = contract;
    this.position = position;
    this.open = open;
    this.openPrice = openPrice;
    this.takeProfit = takeProfit;
    this.orderId = orderId;
  }

  public override bool Equals(object obj) => obj is ComboTradeImpl other && EqualityComparer<Contract>.Default.Equals(contract, other.contract) && position == other.position && open == other.open && openPrice == other.openPrice && takeProfit == other.takeProfit && orderId == other.orderId;

  public override int GetHashCode() {
    var hashCode = 622716466;
    hashCode = hashCode * -1521134295 + EqualityComparer<Contract>.Default.GetHashCode(contract);
    hashCode = hashCode * -1521134295 + position.GetHashCode();
    hashCode = hashCode * -1521134295 + open.GetHashCode();
    hashCode = hashCode * -1521134295 + openPrice.GetHashCode();
    hashCode = hashCode * -1521134295 + takeProfit.GetHashCode();
    hashCode = hashCode * -1521134295 + orderId.GetHashCode();
    return hashCode;
  }

  public void Deconstruct(out Contract contract, out int position, out double open, out double openPrice, out double takeProfit, out int orderId) {
    contract = this.contract;
    position = this.position;
    open = this.open;
    openPrice = this.openPrice;
    takeProfit = this.takeProfit;
    orderId = this.orderId;
  }

  public static implicit operator (Contract contract, int position, double open, double openPrice, double takeProfit, int orderId)(ComboTradeImpl value) => (value.contract, value.position, value.open, value.openPrice, value.takeProfit, value.orderId);
  public static implicit operator ComboTradeImpl((Contract contract, int position, double open, double openPrice, double takeProfit, int orderId) value) => new ComboTradeImpl(value.contract, value.position, value.open, value.openPrice, value.takeProfit, value.orderId);
}