using System;
using System.Linq;

public class Statistics {
  /// <summary>
  /// This is where we test the code
  /// </summary>
  public static void Test(double[] x, double[] y) {
    //double[] x =  { 1,2,3,4,5 };
    //double[] y = { 1, 3, 2, 4, 5 };

    double avgX = x.Average();
    double stdevX = Statistics.GetStdev(x);

    double avgY = y.Average();
    double stdevY = Statistics.GetStdev(y);

    double covXY = 0, pearson = 0;

    Statistics.GetCorrelation(x, y, ref covXY, ref pearson);
    System.Diagnostics.Debug.WriteLine(covXY);
    System.Diagnostics.Debug.WriteLine(pearson);

    Console.Read();
  }

  /// <summary>
  /// Get variance
  /// </summary>
  public static double GetVariance(double[] data) {
    double avg = data.Average();
    double sum = data.Sum(d => Math.Pow(d - avg, 2));
    return sum / data.Length;
  }

  /// <summary>
  /// Get standard deviation
  /// </summary>
  public static double GetStdev(double[] data) {
    return Math.Sqrt(GetVariance(data));
  }

  /// <summary>
  /// Get correlation
  /// </summary>
  public static double GetPearson(double[] x, double[] y) {
    double covXY = 0, pearson = 0;
    GetCorrelation(x, y,ref covXY, ref pearson);
    return pearson;
  }
  public static void GetCorrelation(double[] x, double[] y, ref double covXY, ref double pearson) {
    if (x.Length != y.Length)
      throw new Exception("Length of sources is different");

    double avgX = x.Average();
    double stdevX = GetStdev(x);
    double avgY = y.Average();
    double stdevY = GetStdev(y);

    int len = x.Length;



    for (int i = 0; i < len; i++)
      covXY += (x[i] - avgX) * (y[i] - avgY);

    covXY /= len;

    pearson = covXY / (stdevX * stdevY);
  }
}
