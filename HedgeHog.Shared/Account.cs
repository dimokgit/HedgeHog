using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using HedgeHog.Shared;

namespace HedgeHog.Shared {
  [DataContract]
  public class Account {
    [DataMember]
    public string ID { get; set; }
    [DataMember]
    public double Balance { get; set; }
    [DataMember]
    public double Equity { get; set; }
    [DataMember]
    public double UsableMargin { get; set; }
    [DataMember]
    public bool IsMarginCall { get; set; }
    [DataMember]
    public int PipsToMC { get; set; }
    [DataMember]
    public double DayPL { get; set; }
    [DataMember]
    public bool Hedging { get; set; }
    [DataMember]
    Trade[] _trades = new Trade[] { };
    [DataMember]
    Order[] _orders = null;
    [DataMember]
    public double StopAmount { get; set; }
    [DataMember]
    public double LimitAmount { get; set; }

    public double BalanceOnStop { get { return Balance + StopAmount; } }
    public double BalanceOnLimit { get { return Balance + LimitAmount; } }

    [DataMember]
    public DateTime ServerTime { get; set; }
    [DataMember]
    public WiredException Error { get; set; }


    public Trade[] Trades { get { return _trades; } set { _trades = value; } }
    public Order[] Orders { get { return _orders; } set { _orders = value; } }

    public double PL { get { return Trades.GrossInPips(); } }

    public double Net { get { return Equity - Balance - Trades.Sum(t => t.CommissionByTrade(t)); } }

    public double StopToBalanceRatio { get { return StopAmount / Balance; } }


  }
}
