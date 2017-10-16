using MongoDB.Bson;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data.Entity.Core.EntityClient;
using System.Data.Entity.Core.Objects;
using System.Data.Entity.Core.Objects.DataClasses;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Serialization;

namespace HedgeHog.Alice.Store {
  public partial class TradingAccount  {



    public System.String Password { get; set; }
    public global::System.String MasterId { get; set; }
    public global::System.Boolean IsDemo { get; set; }
    public global::System.String AccountId { get; set; }
    public string AccountSubId { get; set; }
    [JsonIgnore]
    [Key]
    public ObjectId _id { get; set; }
    public global::System.Boolean IsMaster { get; set; }
    public global::System.String TradeRatio { get; set; }
    public global::System.Double Commission { get; set; }
    public global::System.Boolean IsActive { get; set; }
    public Nullable<global::System.Double> PipsToExit { get; set; }
    public global::System.String Currency { get; set; }

    public string Broker { get; set; }
    public CommissionTypes CommissionType { get; set; }
    public enum CommissionTypes { Rel, Abs };
  }
}
