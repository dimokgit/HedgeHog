using HedgeHog;
using IBApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
namespace IBApp {
  public class ComboTrade {
    public bool IsVirtual { get; private set; }
    public double Change => closePrice * position.Sign() - openPrice;
    public double Commission => contract.Legs((c, l) => (c, q: l.Ratio.Abs())).DefaultIfEmpty((c: contract, q: 1))
      .Sum(t => AccountManager.CommissionPerContract(t.c) * t.q) * position.Abs();
    public IEnumerable<ComboTrade> CoveredOption(ComboTrade[] trades) {
      return trades.Where(_ => !contract.IsOption).
        Where(t => t.contract.IsOption 
        && IsBuy == t.contract.IsCall 
        && t.position < 0 
        && t.contract.Expiration <= DateTime.Today.AddDays(1) 
        && t.contract.UnderContract.Any(u => u.Key == contract.Key));
    }

    //new HedgeCombo(hc.contract, pl, openPrice, closePrice, hc.quantity * g.First().position.position.Sign())
    public ComboTrade() {

    }
    public ComboTrade(Contract contract, double pl, double openPrice, double closePrice, int position, double takeProfit, double profit, double open, int orderId) {
      this.contract = contract;
      this.pl = pl;
      this.openPrice = openPrice;
      this.closePrice = closePrice;
      this.position = position;
      this.takeProfit = takeProfit;
      this.profit = profit;
      this.open = open;
      this.orderId = orderId;
    }
    public ComboTrade(Contract contract) {
      this.contract = contract;
      position = 1;
      IsVirtual = true;
    }
    public ComboTrade(IBApi.Contract contract, int position, double open, double close, double pl, double change, double underPrice
      , double strikeAvg, double openPrice, double closePrice, (double bid, double ask) price, double takeProfit, double profit
      , double pmc, double mcUnder, int orderId) {
      this.contract = contract;
      this.position = position;
      this.open = open;
      this.close = close;
      this.pl = pl;
      this.change = change;
      this.underPrice = underPrice;
      this.strikeAvg = strikeAvg;
      this.openPrice = openPrice;
      this.closePrice = closePrice;
      this.price = price;
      this.takeProfit = takeProfit;
      this.profit = profit;
      this.pmc = pmc;
      this.mcUnder = mcUnder;
      this.orderId = orderId;
    }
    public override string ToString() => new {
      this.contract,
      this.position,
      this.open,
      this.close,
      this.pl,
      this.change,
      this.underPrice,
      this.strikeAvg,
      this.openPrice,
      this.closePrice,
      this.price,
      this.takeProfit,
      this.profit,
      this.pmc,
      this.mcUnder,
      this.orderId,
      this.IsBuy
    } + "";
    public Contract contract { get; }
    public int position { get; }
    public double open { get; }
    public double close { get; }
    public double pl { get; }
    public double change { get; }
    public double underPrice { get; }
    public double strikeAvg { get; }
    public double openPrice { get; }
    public double closePrice { get; }
    public (double bid, double ask) price { get; }
    public double takeProfit { get; }
    public double profit { get; }
    public double pmc { get; }
    public double mcUnder { get; }
    public int orderId { get; }
    public bool IsBuy => position > 0;

    public object ToAnon() => new {
      contract,
      pl,
      openPrice,
      closePrice,
      position,
      takeProfit,
      profit,
      open,
      orderId,
      IsBuy
    };
  }
}
