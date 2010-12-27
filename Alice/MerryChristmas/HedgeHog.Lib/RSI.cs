using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;

namespace HedgeHog.Rsi {
  public static class RsiExtensions {

    public static void Rsi(this Rate[] Rates, TimeSpan interval) {
      Rates.Rsi(interval, false);
    }
    public static void Rsi(this Rate[] Rates, TimeSpan interval, bool Refresh) {
      Rates.Rsi(interval, (r, v) => r.PriceRsi = v, r => r.PriceRsi, Refresh);
    }

    public static void Rsi(this Rate[] Rates, int interval) {
      Rates.Rsi(interval, false);
    }
    public static void Rsi(this Rate[] Rates, int interval, bool Refresh) {
      Rates.Rsi(interval, (r, v) => r.PriceRsi = v, r => r.PriceRsi,Refresh);
    }
    public static void Rsi1(this Rate[] Rates, int interval) {
      Rates.Rsi1(interval, false);
    }
    public static void Rsi1(this Rate[] Rates, int interval,bool Refresh) {
      Rates.Rsi(interval, (r, v) => r.PriceRsi1 = v, r => r.PriceRsi1, Refresh);
    }
    public static void Rsi(this Rate[] Rates, int interval, Action<Rate, double?> SetValue, Func<Rate, double?> GetValue) {
      Rates.Rsi(interval, SetValue, GetValue, false);
    }
    public static void Rsi(this Rate[] Rates, TimeSpan interval, Action<Rate, double?> SetValue, Func<Rate, double?> GetValue, bool Refresh) {
      var startDate = Rates.First().StartDate + interval;
      foreach (var rate in Rates.Where(r => r.StartDate > startDate))
        if (Refresh && rate.StartDate <= startDate) SetValue(rate, null);
        else if (Refresh || !GetValue(rate).HasValue)
          SetValue(rate, Rates.Where(interval, rate).ToArray().Rsi());
    }
    public static void Rsi(this Rate[] Rates, int interval, Action<Rate, double?> SetValue, Func<Rate, double?> GetValue, bool Refresh) {
      for (int i = 0; i < Rates.Length; i++)
        if (Refresh && i < interval) SetValue(Rates[i], null);
        else if (Refresh || !GetValue(Rates[i]).HasValue)
          SetValue(Rates[i], Rates.Skip(i - interval).Take(interval).ToArray().Rsi());
    }
    public static double Rsi(this Rate[] Rates) {
      int interval = Rates.Length - 1;
      double[] values = Rates.Select(r => r.PriceClose).ToArray();
      double num = 0.0;
      double num4 = a(values, 0, interval);
      double num5 = b(values, 0, interval);
      num = num4 / num5;
      return 100.0 - (100.0 / (1.0 + num));
    }
    private static double a(double[] A_0, int A_1, int A_2) {
      double num = 0.0;
      for (int i = A_1 + A_2; i > A_1; i--) {
        if ((A_0[i] - A_0[i - 1]) > 0.0) {
          num += A_0[i] - A_0[i - 1];
        }
      }
      return (num / ((double)A_2));
    }
    private static double b(double[] A_0, int A_1, int A_2) {
      double num = 0.0;
      for (int i = A_1 + A_2; i > A_1; i--) {
        if ((A_0[i] - A_0[i - 1]) < 0.0) {
          num += A_0[i - 1] - A_0[i];
        }
      }
      return (num / ((double)A_2));
    }

    /*
    public static List<double> CalculateRSISeries(List<MarketRatePlus> rates, int rsiPeriod, string askOrBid)
            {         

                //calculate 'U' and 'D' series
                List<double> uSeries = new List<double>();
                List<double> dSeries = new List<double>();

                for(int x = 0; x < rates.Count; x++)
                {
                    MarketRatePlus curMarketRate = rates[x];
                
                    double curUVal = calculateUVal(curMarketRate,askOrBid);
                    uSeries.Add(curUVal);

                    double curDVal = calculateDVal(curMarketRate, askOrBid);
                    dSeries.Add(curDVal);
                }
                        
                //calculate EMA series of 'U' and 'D'
                List<double> uEMASeries = MovingAverageCalc.CalculateEMASeries(uSeries, rsiPeriod);
                List<double> dEMASeries = MovingAverageCalc.CalculateEMASeries(dSeries, rsiPeriod);
            
                //calculate RSI series
                List<double> rsiSeries = new List<double>();
                for (int i = 0; i < uEMASeries.Count; i++)
                {
                    double curUEMA = uEMASeries[i];
                    double curDEMA = dEMASeries[i];

                    //double RS = curUEMA / curDEMA;

                    //double RSI = 100 - 100 * (1 / (1 + RS));

                    double RSI = 100 * (curUEMA / (curUEMA + curDEMA));

                    rsiSeries.Add(RSI);
                }

                return rsiSeries;
            }
            private static double calculateUVal(MarketRatePlus curRate, string askOrBid)
            {
                double open = MarketRateUtil.GetRateSegment(curRate,askOrBid,MarketRateUtil.OPEN);
                double close = MarketRateUtil.GetRateSegment(curRate,askOrBid,MarketRateUtil.CLOSE);

                double diff = close - open;

                double U = Math.Max(0, diff);

                return U;
            }
            private static double calculateDVal(MarketRatePlus curRate, string askOrBid)
            {
                double open = MarketRateUtil.GetRateSegment(curRate, askOrBid, MarketRateUtil.OPEN);
                double close = MarketRateUtil.GetRateSegment(curRate, askOrBid, MarketRateUtil.CLOSE);

                double diff = open - close;

                double D = Math.Max(0, diff);

                return D;
            }*/




  }
}
