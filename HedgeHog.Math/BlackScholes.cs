using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public static class BlackScholes {

  public static double CallPrice(double Spot, double Strike, double InterestRate,
                          double Income, double daysTillExpiration, double Volatility) {
    if(Spot <= Strike && daysTillExpiration <= 0) return 0;
    double Expiry = daysTillExpiration / 365;
    double callPrice;
    double a = Math.Log(Spot / Strike);
    double b_call = (InterestRate - Income + 0.5 * Math.Pow(Volatility, 2)) * Expiry;
    double b_put = (InterestRate - Income - 0.5 * Math.Pow(Volatility, 2)) * Expiry;
    double c = Volatility * Math.Sqrt(Expiry);
    double d1 = (a + b_call) / c;
    double d2 = (a + b_put) / c;
    callPrice = Spot * NormsDist(d1) - Strike * Math.Exp(-InterestRate * Expiry) * NormsDist(d2);
    return callPrice;
  }

  public static double PutPrice(double Spot, double Strike, double InterestRate,
                         double Income, double daysTillExpiration, double Volatility) {
    if(Spot >= Strike && daysTillExpiration <= 0) return 0;
    double Expiry = daysTillExpiration / 365;
    double putPrice;
    double a = Math.Log(Spot / Strike);
    double b_call = (InterestRate - Income + 0.5 * Math.Pow(Volatility, 2)) * Expiry;
    double b_put = (InterestRate - Income - 0.5 * Math.Pow(Volatility, 2)) * Expiry;
    double c = Volatility * Math.Sqrt(Expiry);
    double d1 = (a + b_call) / c;
    double d2 = (a + b_put) / c;
    putPrice = Strike * Math.Exp(-InterestRate * Expiry) * NormsDist(-d2) - Spot * NormsDist(-d1);
    return putPrice;
  }

  static double Norm(double z) { //normal probability density function
    double normsdistval = 1 / (Math.Sqrt(2 * Math.PI)) * Math.Exp(-Math.Pow(z, 2) / 2);
    return normsdistval;
  }

  static double NormsDist(double x) { //normal cumulative density function
    const double b0 = 0.2316419;
    const double b1 = 0.319381530;
    const double b2 = -0.356563782;
    const double b3 = 1.781477937;
    const double b4 = -1.821255978;
    const double b5 = 1.330274429;
    double t = 1 / (1 + b0 * x);
    double sigma = 1 - Norm(x) * (b1 * t + b2 * Math.Pow(t, 2) + b3 * Math.Pow(t, 3)
                   + b4 * Math.Pow(t, 4) + b5 * Math.Pow(t, 5));
    return sigma;
  }

}