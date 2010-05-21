using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;

namespace HedgeHog {
  public class Volt {
    public DateTime StartDate { get; set; }
    public int Index { get; set; }
    public double Volts { get; set; }
    public double VoltsPoly { get; set; }
    public double VoltsCMA { get; set; }
    public double AskMax { get; set; }
    public double AskMin { get; set; }
    public double BidMax { get; set; }
    public double BidMin { get; set; }
    public double Price { get; set; }
    public double PriceAvg { get; set; }
    public double PriceAvgAvg { get; set; }
    public double PriceAvg1 { get; set; }
    public Volt() { }
    public Volt(int index, DateTime startDate, double volts, double voltsCMA, double askMax, double askMin, double bidMax, double bidMin, double price, double priceAvg, double priceAvgAvg, double priceAvg1) {
      Index = index;
      StartDate = startDate;
      Volts = volts;
      VoltsCMA = voltsCMA;
      AskMax = askMax;
      AskMin = askMin;
      BidMax = bidMax;
      BidMin = bidMin;
      Price = price;
      PriceAvg = priceAvg;
      PriceAvgAvg = priceAvgAvg;
      PriceAvg1 = priceAvg1;
    }
  }
  public class VoltForGrid {
    [DisplayFormat(DataFormatString = "{0:dd HH:mm}")]
    [DisplayName("Date")]
    public DateTime StartDate { get; set; }
    [DisplayFormat(DataFormatString = "{0:n3}")]
    public double Volts { get; set; }
    public double Average { get { return (AverageAsk + AverageBid) / 2; } }
    public double Average1 { get; set; }
    [DisplayName("")]
    public double AverageAsk { get; set; }
    [DisplayName("")]
    public double AverageBid { get; set; }
    public VoltForGrid() { }
    public VoltForGrid(DateTime startDate, double volts, double averageAsk, double averageBid,double average1) {
      StartDate = startDate;
      Volts = volts;
      AverageAsk = averageAsk;
      AverageBid = averageBid;
      Average1 = average1;
    }
  }
}
