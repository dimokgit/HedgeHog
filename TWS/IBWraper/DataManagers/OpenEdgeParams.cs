using IBApi;
using System.Collections.Generic;
namespace IBApp {
  public struct OpenEdgeParams {
    public Contract contract;
    public int quantity;
    public OrderCondition[] enterConditions;
    public double price;
    public double takeProfit;

    public OpenEdgeParams(Contract contract, int quantity, OrderCondition[] enterConditions, double price, double takeProfit) {
      this.contract = contract;
      this.quantity = quantity;
      this.enterConditions = enterConditions;
      this.price = price;
      this.takeProfit = takeProfit;
    }

    public override bool Equals(object obj) => obj is OpenEdgeParams other && EqualityComparer<Contract>.Default.Equals(contract, other.contract) && quantity == other.quantity && EqualityComparer<OrderCondition[]>.Default.Equals(enterConditions, other.enterConditions) && price == other.price && takeProfit == other.takeProfit;

    public override int GetHashCode() {
      var hashCode = 1872444456;
      hashCode = hashCode * -1521134295 + EqualityComparer<Contract>.Default.GetHashCode(contract);
      hashCode = hashCode * -1521134295 + quantity.GetHashCode();
      hashCode = hashCode * -1521134295 + EqualityComparer<OrderCondition[]>.Default.GetHashCode(enterConditions);
      hashCode = hashCode * -1521134295 + price.GetHashCode();
      hashCode = hashCode * -1521134295 + takeProfit.GetHashCode();
      return hashCode;
    }

    public void Deconstruct(out Contract contract, out int quantity, out OrderCondition[] enterConditions, out double price, out double takeProfit) {
      contract = this.contract;
      quantity = this.quantity;
      enterConditions = this.enterConditions;
      price = this.price;
      takeProfit = this.takeProfit;
    }

    public override string ToString() => new { contract, quantity, price, takeProfit } + "";

    public static implicit operator (Contract contract, int quantity, OrderCondition[] enterConditions, double price, double takeProfit)(OpenEdgeParams value) => (value.contract, value.quantity, value.enterConditions, value.price, value.takeProfit);
    public static implicit operator OpenEdgeParams((Contract contract, int quantity, OrderCondition[] enterConditions, double price, double takeProfit) value) => new OpenEdgeParams(value.contract, value.quantity, value.enterConditions, value.price, value.takeProfit);
  }
}

