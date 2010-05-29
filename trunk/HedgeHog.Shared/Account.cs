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
    public bool Hedging { get; set; }
    [DataMember]
    Trade[] _trades = new Trade[] { };
    [DataMember]
    Order[] _orders = null;
    [DataMember]
    public double StopAmount { get; set; }
    [DataMember]
    public double LimitAmount { get; set; }
    [DataMember]
    public DateTime ServerTime { get; set; }
    [DataMember]
    public WiredException Error { get; set; }


    public Trade[] Trades { get { return _trades; } set { _trades = value; } }
    public Order[] Orders { get { return _orders; } set { _orders = value; } }

    public double PL { get { return Math.Round(Trades.GrossInPips(), 1); } }

    public double Gross { get { return Math.Round(Equity - Balance, 1); } }

    public double StopToBalanceRatio { get { return StopAmount / Balance; } }

  }
}
