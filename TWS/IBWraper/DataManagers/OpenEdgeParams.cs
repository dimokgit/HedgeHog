using HedgeHog;
using IBApi;
using System;
using System.Collections.Generic;
using System.Linq;
using static IBApp.AccountManager;

namespace IBApp {
  public struct OpenEdgeParams {
    public readonly Contract contract;
    public readonly OrderCondition[] enterConditions;
    public readonly double limitPrice;
    public readonly IEnumerable<OrderContractHolder> currentOrders;
    public IEnumerable<OrderContractHolder> replaceOrders {
      get {
        var me = contract;
        var lp = limitPrice;
        return currentOrders.Where(co => co.contract.Key != me.Key || co.order.LmtPrice - lp > 1);
      }
    }

    public OpenEdgeParams(Contract contract, OrderCondition[] enterConditions, double limitPrice, IEnumerable<OrderContractHolder> currentOrders) {
      this.contract = contract;
      this.enterConditions = enterConditions;
      this.limitPrice = limitPrice;
      this.currentOrders = currentOrders;
    }

    public override bool Equals(object obj) => obj is OpenEdgeParams other && EqualityComparer<Contract>.Default.Equals(contract, other.contract) && EqualityComparer<OrderCondition[]>.Default.Equals(enterConditions, other.enterConditions) && limitPrice == other.limitPrice;

    public override int GetHashCode() {
      var hashCode = 1872444456;
      hashCode = hashCode * -1521134295 + EqualityComparer<Contract>.Default.GetHashCode(contract);
      hashCode = hashCode * -1521134295 + EqualityComparer<OrderCondition[]>.Default.GetHashCode(enterConditions);
      hashCode = hashCode * -1521134295 + limitPrice.GetHashCode();
      return hashCode;
    }

    public void Deconstruct(out Contract contract, out OrderCondition[] enterConditions, out double limitPrice) {
      contract = this.contract;
      enterConditions = this.enterConditions;
      limitPrice = this.limitPrice;
    }

    public override string ToString() => new { contract, limitPrice } + "";

  }
}

