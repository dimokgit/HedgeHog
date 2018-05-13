using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Store {
  public class TradePersist {
    [Key]
    public ObjectId _id { get; set; }
    public String Symbol { get; set; }
    public Double OpenPrice { get; set; }
    public Double ClosePrice { get; set; }
    public Double Open { get; set; }
    public Double Close { get; set; }
    public Double Quantity { get; set; }
    public DateTime TimeOpen { get; set; }
    public DateTime TimeClose { get; set; }
    public Double PL { get; set; }
    public TradePersist() { }
    public TradePersist(String symbol, Double openPrice, Double closePrice, Double open, Double close, Double quantity, DateTime timeOpen, DateTime timeClose,Double pl) {
      Symbol=symbol;
      OpenPrice = openPrice;
      ClosePrice = closePrice;
      Open = open;
      Close = close;
      Quantity = quantity;
      TimeOpen = timeOpen;
      TimeClose = timeClose;
      PL = pl;
    }
  }
}
