using HedgeHog;
using HedgeHog.Core;
using HedgeHog.Shared;
using IBApi;
using IBSampleApp.messages;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
namespace IBApp {
  public partial class AccountManager {
    public class OrderContractHolder :IEquatable<OrderContractHolder> {
      public struct Status {
        public readonly string status;
        public readonly double filled;
        public readonly double remaining;

        public Status(string status, double filled, double remaining) {
          this.status = status;
          this.filled = filled;
          this.remaining = remaining;
        }
        public override string ToString() => $"{status}:{filled}<{remaining}";
      }
      public readonly IBApi.Order order;
      public readonly IBApi.Contract contract;
      public Status status { get; set; }

      private readonly IDisposable _shouldExecuteDisposable;

      public int OrderId => order.OrderId;
      public bool isDone => (status.status, status.remaining).IsOrderDone();
      public bool isNew => status.status == "new";
      public bool isSubmitted => status.status == "Submitted";
      public bool isFilled => status.status == "Filled";
      public bool isPreSubmitted => status.status == "PreSubmitted";
      public bool isInactive => status.status == "Inactive";
      public bool hasSubmitted => isSubmitted || isPreSubmitted;
      public bool ShouldExecute { get; private set; }
      public OrderContractHolder() {
        return;
        try {
          var uo = (
            from c in GetContract().ToObservable()
            where c.SecType != "BAG"
            from uc in c.UnderContract
            from cd in IBClientCore.IBClientCoreMaster.ReqContractDetailsCached(uc)
            select cd.Contract
            );
          _shouldExecuteDisposable = (
            from p in IBClientCore.IBClientCoreMaster.PriceChangeObservable.Select(p => p.EventArgs.Price)
            from uc in uo
            where p.Pair == uc.LocalSymbol
            select p
            ).Subscribe(p => ShouldExecute = ShouldExecuteImpl(order.IsBuy ? p.Ask : p.Bid), exc => { Debugger.Break(); }, () => Debugger.Break());
        } catch(Exception exc) {
          LogMessage.Send(exc);
        }
        IEnumerable<Contract> GetContract() {
          if(contract != null)
            yield return contract;
        }
        bool ShouldExecuteImpl(double underPrice) =>
          (from pc in order.Conditions.OfType<PriceCondition>()
           where underPrice > pc.Price && pc.IsMore || underPrice <= pc.Price && !pc.IsMore
           select pc
          ).Any();
      }
      private OrderContractHolder(IBApi.Order order, IBApi.Contract contract) : this() {
        this.order = order;
        this.contract = contract;
        status = new Status("new", 0, order.TotalQuantity);
      }
      public OrderContractHolder(IBApi.Order order, IBApi.Contract contract, string status) : this() {
        this.order = order;
        this.contract = contract;
        this.status = new Status(status, 0, order.TotalQuantity);
      }
      public OrderContractHolder(IBApi.Order order, IBApi.Contract contract, string status, double filled, double remaining) : this() {
        this.order = order;
        this.contract = contract;
        this.status = new Status(status, filled, remaining);
      }
      ~OrderContractHolder() {
        _shouldExecuteDisposable?.Dispose();
      }

      public static implicit operator OrderContractHolder(OpenOrderMessage p) => new OrderContractHolder(p.Order, p.Contract, p.OrderState.Status);
      //public static explicit operator OrderContractHolder(OpenOrderMessage p) => new OrderContractHolder(p.Order, p.Contract, p.OrderState.Status);
      public bool Equals(OrderContractHolder other) => order + "," + contract == other.order + "," + other.contract;
      public override string ToString() => ToStringImpl();
      string Key => $"{order.Key}::{contract.Key}";
      string ToStringImpl() => $"{order.ActionText}::{contract.ShortSmart}::{order.TypeText}[{order.TotalQuantity}]{status.status}:{status.filled.ToInt()}<{status.remaining.ToInt()}:id={order.OrderId}";
    }
  }
}
