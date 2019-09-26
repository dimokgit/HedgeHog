using HedgeHog;
using HedgeHog.Shared;
using IBApi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace IBApp {
  public class Position {
    public Position(PositionMessage p) {
      this.contract = p.Contract;
      position = p.Position.ToInt();
      averageCost = p.AverageCost;
      open = p.AverageCost * position;
      pipCost = contract.ComboMultiplier * position.Abs();
      price = averageCost / contract.ComboMultiplier;
    }

    public Contract contract { get; }
    public int position { get; }
    public int Quantity => position.Abs();
    public bool IsBuy => position > 0;
    public double averageCost { get; }
    public double open { get; }
    public double pipCost { get; }
    public double price { get; }
    public double AvgCost { get; }

    public override string ToString() => new { contract, position, averageCost, open, price, pipCost } + "";
  }
  public partial class AccountManager {
    public Position ContractPosition(PositionMessage p) => new Position(p);

    ConcurrentDictionary<string, Position> _positions = new ConcurrentDictionary<string, Position>();
    public ICollection<Position> Positions => _positions.Values;
    //public Subject<ICollection<(Contract contract, int position, double open)>> ContracPositionsSubject = new Subject<ICollection<(Contract contract, int position, double open)>>();

    void OnPosition(PositionMessage p) {
      var posMsg = new PositionMessage("", p.Contract, p.Position, p.AverageCost);
      if(p.Position == 0) {
        OpenTrades
         .Where(t => t.Pair == p.Contract.LocalSymbol)
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
          .DefaultIfEmpty(() => p.Contract.SideEffect(c
          => OpenTrades.Add(TradeFromPosition(c, p.Position, p.AverageCost)
          .SideEffect(t => _verbous(new { OpenPosition = new { t.Pair, t.IsBuy, t.Lots } })))))
          .ToList()
          .ForEach(a => a());
      }

      TraceTrades("OnPositions: ", OpenTrades);
      //if(IbClient.ClientId == 0 && !_positions.Values.Any(p => p.position != 0))
      //  CancelAllOrders("Canceling stale orders");
    }

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
