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
    public Position(Contract contract, double position, double averageCost) {
      this.contract = contract;
      this.position = position.ToInt();
      this.averageCost = averageCost;
      open = averageCost * position;
      pipCost = contract.ComboMultiplier * position.Abs();
      price = averageCost / contract.ComboMultiplier;
    }
    public Contract contract { get; }
    public int position { get; }
    public int Quantity => position.Abs();
    public double EntryPrice =>
      contract.IsOption
      ? contract.Strike - price * contract.Delta(position)
      : price * contract.Delta(position);
      
      
    public bool IsBuy => position > 0;
    public double averageCost { get; }
    public double open { get; }
    public double pipCost { get; }
    public double price { get; }
    public double AvgCost { get; }

    private int rightSign => contract.Right == "P" ? 1 : contract.Right == "C" ? -1 : 0;
    public override string ToString() => new { contract, position, averageCost, open, price, pipCost } + "";
    public bool Compare(Contract contract, int positionSign) =>
      this.contract.Symbol == contract.Symbol
      && this.contract.Delta(this.position.Sign()) == contract.Delta(positionSign);
    //|| contract.LocalSymbol == localSymbol

    private static int delta(Contract c) => (c.IsOption ? 1 : -1) ;

  }
  public static class PositionsMixin {
    internal static bool compIgnoreCase(this string s1, string s2) => (s1?.Equals(s2, StringComparison.OrdinalIgnoreCase)).GetValueOrDefault();
    public static IEnumerable<(string Symbol, string CP, int positionSign, double EntryPrice, int quantity)> EntryPrices(this IEnumerable<Position> positions) {
      // sync this with position.Compare
      var gs = positions.GroupBy(p => (p.contract.Symbol, p.contract.Right, posSign: p.position.Sign()));

      return gs.Select(g => {
        var ep = g.EntryPrice();
        return (g.Key.Symbol, g.Key.Right, g.Key.posSign, ep.entryPrice, ep.quantity);
      });
    }
    public static (double entryPrice, int quantity) EntryPrice(this IEnumerable<Position> positions) {
      var s = positions.Sum(p => p.EntryPrice * p.Quantity);
      var q = positions.Sum(p => p.Quantity);
      return (s / q, q);
    }
    public static IList<(double entryPrice, int quantity)> EntryPrice(this IEnumerable<Position> positions, Func<Position, bool> filter) {
      var pos = positions.Where(filter).ToList();
      if(pos.Count == 0) return Enumerable.Empty<(double entryPrice, int quantity)>().ToList();
      var s = pos.Sum(p => p.EntryPrice * p.Quantity);
      var q = pos.Sum(p => p.Quantity);
      return new[] { (s / q, q) };
    }

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

      //TraceTrades("OnPositions: ", OpenTrades);
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
      var commission = contract.IsStock ? 0.016 : 0.85 * 5;
      trade.CommissionByTrade = t => t.Lots * commission;
      return trade;
    }
  }
}
