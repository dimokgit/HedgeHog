﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
[assembly:CLSCompliant(true)]
namespace HedgeHog.Bars {
  public enum FractalType {None = 0, Buy = -1, Sell = 1 };
  public abstract class BarBase : IEquatable<BarBase> {
    public DateTime StartDate { get; set; }
    public readonly bool IsHistory;

    public double AskHigh { get; set; }
    public double AskLow { get; set; }
    public double BidHigh { get; set; }
    public double BidLow { get; set; }
    public double AskClose { get; set; }
    public double AskOpen { get; set; }
    public double BidClose { get; set; }
    public double BidOpen { get; set; }

    public double Spread { get { return (AskHigh - AskLow + BidHigh - BidLow) / 2; } }
    public double SpreadMax { get { return Math.Max(AskHigh - AskLow, BidHigh - BidLow); } }
    public double SpreadMin { get { return Math.Min(AskHigh - AskLow, BidHigh - BidLow); } }
    public double PriceHigh { get { return (AskHigh + BidHigh) / 2; } }
    public double PriceLow { get { return (AskLow + BidLow) / 2; } }
    public double PriceClose { get { return (AskClose + BidClose) / 2; } }
    public double PriceOpen { get { return (AskOpen + BidOpen) / 2; } }
    public double PriceAvg { get { return (PriceHigh + PriceLow + PriceOpen + PriceClose) / 4; } }

    public double PriceAvg1 { get; set; }
    public double PriceAvg2 { get; set; }
    public double PriceAvg3 { get; set; }
    public double PriceAvg4 { get; set; }
    public double PriceWave { get; set; }
    public double? PriceRsi { get; set; }
    public double PriceRsiP { get; set; }
    public double PriceRsiN { get; set; }
    public double? PriceRsiCR { get; set; }
    public double? PriceRlw { get; set; }
    public double? PriceTsi { get; set; }
    public double? PriceTsiCR { get; set; }
    public double[] PriceCMA { get; set; }
    public double PriceStdDev { get; set; }
    public FractalType Fractal {
      get { return (int)FractalSell + FractalBuy; }
      set {
        if (value == FractalType.None) FractalBuy = FractalSell = FractalType.None;
        else if (value == FractalType.Buy) FractalBuy = value;
        else FractalSell = value;
      }
    }
    public FractalType FractalBuy { get; set; }
    public FractalType FractalSell { get; set; }
    public double? FractalPrice {
      get { 
        return Fractal == FractalType.None ? (double?)null: Fractal == FractalType.Buy? BidLow :  AskHigh; }
    }
    public bool HasFractal { get { return Fractal != FractalType.None; } }
    public bool HasFractalSell { get { return FractalSell == FractalType.Sell; } }
    public bool HasFractalBuy { get { return FractalBuy == FractalType.Buy; } }

    public int Count { get; set; }

    public BarBase() { }
    public BarBase(bool isHistory) { IsHistory = isHistory; }

    void SetAsk(double ask) { AskOpen = AskLow = AskClose = AskHigh = ask; }
    void SetBid(double bid) { BidOpen = BidLow = BidClose = BidHigh = bid; }

    public BarBase(DateTime startDate, double ask, double bid, bool isHistory) {
      SetAsk(ask);
      SetBid(bid);
      StartDate = startDate;
      IsHistory = isHistory;
    }
    public void AddTick(DateTime startDate, double ask, double bid) {
      if (Count++ == 0) {
        SetAsk(ask);
        SetBid(bid);
        StartDate = startDate.Round();
      } else {
        if (ask > AskHigh) AskHigh = ask;
        if (ask < AskLow) AskLow = ask;
        if (bid > BidHigh) BidHigh = bid;
        if (bid < BidLow) BidLow = bid;
      }
      AskClose = ask;
      BidClose = bid;
    }


    public static bool operator ==(BarBase b1, BarBase b2) { return (object)b1 == null && (object)b2 == null ? true : (object)b1 == null ? false : b1.Equals(b2); }
    public static bool operator !=(BarBase b1, BarBase b2) { return (object)b1 == null ? (object)b2 == null ? false : !b2.Equals(b1) : !b1.Equals(b2); }
    public static TBar BiggerFractal<TBar>(TBar b1, TBar b2) where TBar : BarBase{
      if (b1.Fractal == b2.Fractal) {
        if (b1.Fractal == FractalType.Buy) return b1.FractalPrice < b2.FractalPrice ? b1 : b2;
        if (b1.Fractal == FractalType.Sell) return b1.FractalPrice > b2.FractalPrice ? b1 : b2;
        return null;
      } else return null;
    }

    public override string ToString() {
      return string.Format("{0:dd HH:mm:ss}:{1}/{2}", StartDate, AskHigh, BidLow);
    }
    public override bool Equals(object obj) {
      return obj is BarBase ? Equals(obj as BarBase) : false;
    }
    public virtual bool Equals(BarBase other) {
      if ((object)other == null || StartDate != other.StartDate) return false;
      return true;
    }
    public override int GetHashCode() { return StartDate.GetHashCode(); }

    static double MA(double dOld, double dNew, int count) { return (dOld * count + dNew) / (count + 1); }
  }
  public class DataPoint {
    public double Value { get; set; }
    public DataPoint Next { get; set; }
    public DateTime Date { get; set; }
    public int Index { get; set; }
    public int Slope { get { return Math.Sign(Next.Value - Value); } }
  }
  public static class Extensions {
    public static void SetCMA<TBars>(this IEnumerable<TBars> ticks, int cmaPeriod) where TBars : BarBase {
      double? cma1 = null;
      double? cma2 = null;
      double? cma3 = null;
      ticks.ToList().ForEach(t => {
        t.PriceCMA = new double[3];
        t.PriceCMA[2] = (cma3 = Lib.CMA(cma3, cmaPeriod, (cma2 = Lib.CMA(cma2, cmaPeriod, (cma1 = Lib.CMA(cma1, cmaPeriod, t.PriceAvg)).Value)).Value)).Value;
        t.PriceCMA[1] = cma2.Value;
        t.PriceCMA[0] = cma1.Value;
      });
    }
    public static DataPoint[] GetCurve(IEnumerable<BarBase> ticks, int cmaPeriod) {
      double? cma1 = null;
      double? cma2 = null;
      double? cma3 = null;
      int i = 0;
      return (from tick in ticks
              select
              new DataPoint() {
                Value = (cma3 = Lib.CMA(cma3, cmaPeriod, (cma2 = Lib.CMA(cma2, cmaPeriod, (cma1 = Lib.CMA(cma1, cmaPeriod, tick.PriceAvg)).Value)).Value)).Value,
                Date = tick.StartDate,
                Index = i++
              }
                  ).ToArray();
    }
  }
}