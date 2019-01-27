using HedgeHog;
using HedgeHog.Shared;
using IBApi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace IBApp {
  public partial class AccountManager {
    public static bool NoPositionsPlease = false;

    public (Contract contract, int position, double open, double price, double pipCost)
      ContractPosition((IBApi.Contract contract, double pos, double avgCost) p) =>
       (p.contract, position: p.pos.ToInt(), open: p.avgCost * p.pos, p.avgCost / p.contract.ComboMultiplier, pipCost: 0.01 * p.contract.ComboMultiplier * p.pos.Abs());

    ConcurrentDictionary<string, (Contract contract, int position, double open, double price, double pipCost)> _positions = new ConcurrentDictionary<string, (Contract contract, int position, double open, double price, double pipCost)>();
    public ICollection<(Contract contract, int position, double open, double price, double pipCost)> Positions => _positions.Values;
    //public Subject<ICollection<(Contract contract, int position, double open)>> ContracPositionsSubject = new Subject<ICollection<(Contract contract, int position, double open)>>();

    void OnPosition(Contract contract, double position, double averageCost) {
      var posMsg = new PositionMessage("", contract, position, averageCost);
      if(position == 0) {
        OpenTrades
         .Where(t => t.Pair == contract.LocalSymbol)
         .ToList()
         .ForEach(ot => OpenTrades.Remove(ot)
         .SideEffect(_ => _verbous(new { RemovedPosition = new { ot.Pair, ot.IsBuy, ot.Lots } })));
      } else {
        OpenTrades
          .Where(IsEqual2(posMsg))
         .ToList()
         .ForEach(ot => OpenTrades.Remove(ot)
         .SideEffect(_ => _verbous(new { RemovedPosition = new { ot.Pair, ot.IsBuy, ot.Lots } })));
        OpenTrades
          .Where(IsEqual(posMsg))
          .Select(ot
            => new Action(() => ot.Lots = posMsg.Quantity
            .SideEffect(Lots => _verbous(new { ChangePosition = new { ot.Pair, ot.IsBuy, Lots } })))
            )
          .DefaultIfEmpty(() => contract.SideEffect(c
          => OpenTrades.Add(TradeFromPosition(Subscribe(c), position, averageCost)
          .SideEffect(t => _verbous(new { OpenPosition = new { t.Pair, t.IsBuy, t.Lots } })))))
          .ToList()
          .ForEach(a => a());
      }

      TraceTrades("OnPositions: ", OpenTrades);
      var cp = ContractPosition((contract, position, averageCost));
      _positions.AddOrUpdate(cp.contract.Key, cp, (k, v) => cp);
      //if(IbClient.ClientId == 0 && !_positions.Values.Any(p => p.position != 0))
      //  CancelAllOrders("Canceling stale orders");
    }

    private Contract Subscribe(Contract c) => IbClient.SetContractSubscription(c);

    Trade TradeFromPosition(Contract contract, double position, double avgCost) {
      var st = IbClient.ServerTime;
      var trade = CreateTrade(contract.LocalSymbol);
      trade.Id = DateTime.Now.Ticks + "";
      trade.Buy = position > 0;
      trade.IsBuy = trade.Buy;
      trade.Time2 = st;
      trade.Time2Close = IbClient.ServerTime;
      trade.Open = avgCost;
      trade.Lots = position.Abs().ToInt();
      trade.OpenOrderID = "";
      trade.CommissionByTrade = CommissionByTrade;
      return trade;
    }
  }
}
