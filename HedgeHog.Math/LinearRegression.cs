using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {

  /// <summary>
  /// MS Excel algorithm
  /// </summary>
  public static class LinearRegression {
    public delegate T LinearRegressionMap<T>(double intercept, double slope);
    public static double[] Linear(this IList<double> data) {
      var slope = 0.0;
      return new[] { GetIntercept(data, out slope), slope };
    }
    public static double LinearSlope(this IList<double> data) {
      double avgY;
      return GetSlope(data, out avgY);
    }
    public static T Linear<T>(this IList<double> data, LinearRegressionMap<T> map) {
      var slope = 0.0;
      return map(GetIntercept(data, out slope), slope);
    }
    static double GetSlope(IList<double> yArray, out double averageY) {
      double n = yArray.Count;
      double sumxy = 0, sumx = 0, sumy = 0, sumx2 = 0;
      for (int i = 0; i < yArray.Count; i++) {
        sumxy += (double)i * yArray[i];
        sumx += i;
        sumy += yArray[i];
        sumx2 += (double)i * i;
      }
      return ((sumxy - sumx * (averageY = sumy / n)) / (sumx2 - sumx * sumx / n));
    }
    static double GetIntercept(IList<double> data, out double slope) {
      if (data.Count == 0) {
        slope = double.NaN;
        return double.NaN;
      }
      if (data.Count == 1) {
        slope = double.NaN;
        return data[0];
      }

      double avgY;
      slope = GetSlope(data, out avgY);
      return Intecsept(slope, avgY, data.Count);
    }
    static double Intecsept(double slope, double yAverage, int dataLength) {
      return yAverage - slope * (dataLength - 1) / 2.0;
    }
  }
}
/*
public bool Regress(double[,] XY, int order)
{
int countXY = XY.GetLength(0);

double[,] x = new double[order + 1, countXY];
double[] y = new double[countXY];
double[] w = new double[countXY];
 
for (int i = 0; i < countXY; i++)
{
for (int j = 0; j <= order; j++)
{
x[j, i] = Math.Pow(XY[i, 0], j);
}
 
y[i] = XY[i, 1];
w[i] = 1;
}
 
return Regress(y, x, w);
}*/