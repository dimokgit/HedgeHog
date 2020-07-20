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
    public readonly double currentPrice;
    public readonly double currentTheta;
    public readonly IEnumerable<OrderContractHolder> currentOrders;
    public readonly string levelError;
    public bool hasError;

    public IEnumerable<OrderContractHolder> replaceOrders {
      get {
        var me = contract;
        return currentOrders.Where(co => co.contract.Key != me.Key);
      }
    }

    public OpenEdgeParams(Contract contract, OrderCondition[] enterConditions, double currentPrice, double currentTheta, IEnumerable<OrderContractHolder> currentOrders, string levelError) {
      this.contract = contract;
      this.enterConditions = enterConditions;
      this.currentPrice = currentPrice;
      this.currentTheta = currentTheta;
      this.currentOrders = currentOrders;
      this.levelError = levelError;
      this.hasError = !levelError.IsNullOrWhiteSpace();
    }

    public override bool Equals(object obj) => obj is OpenEdgeParams other && EqualityComparer<Contract>.Default.Equals(contract, other.contract) && EqualityComparer<OrderCondition[]>.Default.Equals(enterConditions, other.enterConditions) && currentPrice == other.currentPrice;

    public override int GetHashCode() {
      var hashCode = 1872444456;
      hashCode = hashCode * -1521134295 + EqualityComparer<Contract>.Default.GetHashCode(contract);
      hashCode = hashCode * -1521134295 + currentPrice.GetHashCode();
      return hashCode;
    }

    public void Deconstruct(out Contract contract, out OrderCondition[] enterConditions, out double currentPrice) {
      contract = this.contract;
      enterConditions = this.enterConditions;
      currentPrice = this.currentPrice;
    }

    public override string ToString() => new { contract, currentPrice, currentTheta } + "";

  }
}

