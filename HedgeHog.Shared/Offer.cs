using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
/*
    public static Offer[] GetOffers() {
      return (from t in GetRows(TABLE_OFFERS)
              select new Offer() {
                OfferID = (String)t.CellValue("OfferID"),
                Pair = (String)t.CellValue("Instrument"),
                InstrumentType = (int)t.CellValue("InstrumentType"),
                Bid = (Double)t.CellValue("Bid"),
                Ask = (Double)t.CellValue("Ask"),
                Hi = (Double)t.CellValue("Hi"),
                Low = (Double)t.CellValue("Low"),
                IntrS = (Double)t.CellValue("IntrS"),
                IntrB = (Double)t.CellValue("IntrB"),
                ContractCurrency = (String)t.CellValue("ContractCurrency"),
                ContractSize = (int)t.CellValue("ContractSize"),
                Digits = (int)t.CellValue("Digits"),
                DefaultSortOrder = (int)t.CellValue("DefaultSortOrder"),
                PipCost = (Double)t.CellValue("PipCost"),
                MMR = (Double)t.CellValue("MMR"),
                Time = (DateTime)t.CellValue("Time"),
                BidChangeDirection = (int)t.CellValue("BidChangeDirection"),
                AskChangeDirection = (int)t.CellValue("AskChangeDirection"),
                HiChangeDirection = (int)t.CellValue("HiChangeDirection"),
                LowChangeDirection = (int)t.CellValue("LowChangeDirection"),
                QuoteID = (String)t.CellValue("QuoteID"),
                BidID = (String)t.CellValue("BidID"),
                AskID = (String)t.CellValue("AskID"),
                BidExpireDate = (DateTime)t.CellValue("BidExpireDate"),
                AskExpireDate = (DateTime)t.CellValue("AskExpireDate"),
                BidTradable = (String)t.CellValue("BidTradable"),
                AskTradable = (String)t.CellValue("AskTradable"),
                PointSize = (Double)t.CellValue("PointSize"),
              }).ToArray();
    }
 */
namespace HedgeHog.Shared {
  [DataContract]
  public class Offer {
    [DataMember]
    public String OfferID { get; set; }
    [DataMember]
    public String Pair { get; set; }
    [DataMember]
    public int InstrumentType { get; set; }
    [DataMember]
    public Double Bid { get; set; }
    [DataMember]
    public Double Ask { get; set; }
    [DataMember]
    public Double Hi { get; set; }
    [DataMember]
    public Double Low { get; set; }
    [DataMember]
    public Double IntrS { get; set; }
    [DataMember]
    public Double IntrB { get; set; }
    [DataMember]
    public String ContractCurrency { get; set; }
    [DataMember]
    public int ContractSize { get; set; }
    [DataMember]
    public int Digits { get; set; }
    [DataMember]
    public int DefaultSortOrder { get; set; }
    [DataMember]
    public Double PipCost { get; set; }
    [DataMember]
    public Double MMRLong { get; set; }
    double _MMRShort=double.NaN;
    [DataMember]
    public double MMRShort {
      get { return double.IsNaN(_MMRShort) ? MMRLong : _MMRShort; }
      set {
        _MMRShort = value;
      }
    }
    [DataMember]
    public DateTime Time { get; set; }
    [DataMember]
    public int BidChangeDirection { get; set; }
    [DataMember]
    public int AskChangeDirection { get; set; }
    [DataMember]
    public int HiChangeDirection { get; set; }
    [DataMember]
    public int LowChangeDirection { get; set; }
    [DataMember]
    public String QuoteID { get; set; }
    [DataMember]
    public String BidID { get; set; }
    [DataMember]
    public String AskID { get; set; }
    [DataMember]
    public DateTime BidExpireDate { get; set; }
    [DataMember]
    public DateTime AskExpireDate { get; set; }
    [DataMember]
    public String BidTradable { get; set; }
    [DataMember]
    public String AskTradable { get; set; }
    [DataMember]
    public Double PointSize { get; set; }
  }
}
