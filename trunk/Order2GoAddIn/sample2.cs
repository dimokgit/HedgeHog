using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using HedgeHog.Bars;
using FXW = Order2GoAddIn.FXCoreWrapper;

namespace Order2GoAddIn {
  public class IndicatorPoint {
    public DateTime Time { get; set; }
    public double Point { get; set; }
    public IndicatorPoint() {}
    public IndicatorPoint(DateTime time,double point) {
      Time = time;
      Point = point;
    }
    public override string ToString() {
      return string.Format("{0:dd HH:mm:ss}:{1}", this.Time, this.Point);
    }
  }
  public class IndicatorPoint2 : IndicatorPoint {
    public double Point1 { get; set; }
    public IndicatorPoint2(DateTime time, double point, double point1) {
      Time = time;
      Point = point;
      Point1 = point1;
    }
    public override string ToString() {
      return string.Format("{0:dd HH:mm:ss}:{1}/{2}", this.Time, this.Point, this.Point1);
    }
  }
  public class IndicatorMACD : IndicatorPoint {
    public double MACD { get; set; }
    public double Signal { get; set; }
    public double Histogram { get; set; }
    public IndicatorMACD(DateTime time, double point, double macd,double signal,double histogram) {
      Time = time;
      Point = point;
      MACD = macd;
      Signal = signal;
      Histogram = histogram;
    }
    public override string ToString() {
      return string.Format("{0:t}:{1}-{2},{3},{4}", this.Time, this.Point, this.MACD,Signal,Histogram);
    }
  }

  public static class Indicators {
    static string path = (string)Registry.GetValue("HKEY_LOCAL_MACHINE\\Software\\CandleWorks\\FXOrder2Go", "InstallPath", "");
    static Indicore.IndicoreManagerAut core = new Indicore.IndicoreManagerAut();
    static Indicators() {
      object errors;
      if (!core.LoadFromCAB(path + "\\indicators\\indicators.cab", out errors))
        throw new Exception("Load standard: "+ errors);
      if (!core.LoadFromFolder(@"Z:\Data\Install\Forex", out errors))
        throw new Exception("Load custom: "+ errors);
    }
    static T ReturnAnonymous<T>(T type) {
      return (T)(object)new { City = "Prague", Name = "Tomas" };
    }
    public static IEnumerable<IndicatorPoint> RLW(Rate[] rates) { return RLW(rates, 14); }
    public static IEnumerable<IndicatorPoint> RLW(Rate[] rates, int period) {
      Indicore.BarSourceAut source = CreateBarsData(rates);
      Indicore.IndicatorInstanceAut instance = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("RLW", source, period, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance.Output)[0];
      instance.Update(true);
      return output.Cast<double>().Select((d, i) => new IndicatorPoint(rates[i].StartDate, d));
    }
    public static IEnumerable<IndicatorPoint> TSI(IList<Rate> rates) {
      Indicore.BarSourceAut source = CreateBarsData(rates);
      Indicore.IndicatorInstanceAut instance = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("TSI", source, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance.Output)[0];
      instance.Update(true);
      return output.Cast<double>().Select((d, i) => new IndicatorPoint(rates[i].StartDate, d));
    }
    public static void FillFractal(this Rate[] rates,Action<Rate,double?> setValue) {
      Indicore.BarSourceAut source = CreateBarsData(rates);
      Indicore.IndicatorInstanceAut instance = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("FRACTAL", source, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputCollectionAut outputs = (Indicore.IndicatorOutputCollectionAut)instance.Output;
      instance.Update(true);
      foreach (var rate in rates) 
        setValue(rate, 0);
      foreach (var output in outputs.Cast<Indicore.IndicatorOutputAut>()) {
        var i = 0;
        output.Cast<double>().ToList().ForEach(d => { if (d != 0) setValue(rates[i], d); i++; });
      }
    }

    private static bool CleanFractals(List<Rate> fractals) {
      Func<Rate, Rate, Rate> compareLambda = (f1, f2) => {
        var ff = new[] { f1, f2 };
        return f1.FractalBuy != 0 ? ff.OrderBy(f => f.BidLow).Last() : ff.OrderBy(f => f.AskHigh).First();
      };
      var delete = fractals.Skip(1).Select((f, i) => new { f1 = f, f2 = fractals[i] }).Where(f => f.f1.Fractal == f.f2.Fractal)
        .Select(f => compareLambda(f.f1, f.f2)).ToArray();
      if (delete.Length > 0) {
        var fractalsList = fractals.ToList();
        delete.ToList().ForEach(d => fractals.Remove(d));
        return true;
      }
      return false;
    }
    public static List<Rate> HasFractal(this IEnumerable<Rate> rates) {
      var fractals = rates.Where(r => r.HasFractal).ToList();
      if (fractals.Count > 1) CleanFractals(fractals);
      return fractals;
    }
    public static IEnumerable<Rate> HasFractal(this IEnumerable<Rate> rates, Func<Rate, bool> filter) {
      return rates.Where(filter).HasFractal();
    }
    public static Rate[] FillFractals(this Rate[] rates) {
      for (int i = 4; i < rates.Length; i++) {
        UpdateFractal(rates, i);
      }
      return rates;
    }
    static void UpdateFractal(Rate[] rates, int period) {
      if (period > 3) {
        var curr = rates[period - 2].BidHigh;
        if (curr > rates[period - 4].BidHigh && curr > rates[period - 3].BidHigh &&
            curr > rates[period - 1].BidHigh && curr > rates[period].BidHigh) {
          rates[period - 2].FractalSell = HedgeHog.Bars.FractalType.Sell;
        } else
          rates[period - 2].FractalSell = HedgeHog.Bars.FractalType.None;
        curr = rates[period - 2].AskLow;
        if (curr < rates[period - 4].AskLow && curr < rates[period - 3].AskLow &&
            curr < rates[period - 1].AskLow && curr < rates[period].AskLow)
          rates[period - 2].FractalBuy = HedgeHog.Bars.FractalType.Buy;
        else
          rates[period - 2].FractalBuy = HedgeHog.Bars.FractalType.None;
      }
    }

    public static IEnumerable<IndicatorPoint2> TSI_CR(Rate[] rates) {
      Indicore.BarSourceAut source = CreateBarsData(rates);
      Indicore.IndicatorInstanceAut instance1 = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("TSI", source, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output1 = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance1.Output)[0];
      Indicore.IndicatorInstanceAut instance2 = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("CR", output1, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output2 = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance2.Output)[0];
      instance1.Update(true);
      instance2.Update(true);
      return output2.Cast<double>().Select((d, i) => new IndicatorPoint2(rates[i].StartDate, output1[i], d));
    }
    public static IEnumerable<IndicatorPoint> RSI(IList<Rate> rates, Func<Rate, double> price, int period) {
      Indicore.TickSourceAut source = CreateTicksData(rates, price);
      Indicore.IndicatorInstanceAut instance = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("RSI", source, period, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance.Output)[0];
      instance.Update(true);
      return output.Cast<double>().Select((d, i) => new IndicatorPoint(rates[i].StartDate, d));
    }
    public static void CR<T>(this Rate[] rates, Func<Rate, double> sourceLambda,Action<Rate,T>destinationLambda) {
      Indicore.TickSourceAut source = CreateTicksData(rates, sourceLambda);
      Indicore.IndicatorInstanceAut instance = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("CR", source, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance.Output)[0];
      instance.Update(true);
      var i = 0;
      output.Cast<T>().ToList().ForEach(d => destinationLambda(rates[i++], d));
    }
    public static IEnumerable<IndicatorPoint2> RSI_CR(IList<Rate> rates, Func<Rate, double> price, int period) {
      Indicore.TickSourceAut source = CreateTicksData(rates, price);
      Indicore.IndicatorInstanceAut instance1 = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("RSI", source, period, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output1 = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance1.Output)[0];
      Indicore.IndicatorInstanceAut instance2 = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("CR", output1, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output2 = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance2.Output)[0];
      instance1.Update(true);
      instance2.Update(true);
      return output2.Cast<double>().Select((d, i) => new IndicatorPoint2(rates[i].StartDate, output1[i], d));
    }
    public static IEnumerable<IndicatorMACD> RsiMACD(Rate[] rates, Func<Rate, double> price, int period) {
      Indicore.TickSourceAut source = CreateTicksData(rates,price);
      Indicore.IndicatorInstanceAut instance1 = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("RSI", source, period, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output1 = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance1.Output)[0];
      Indicore.IndicatorInstanceAut instance2 = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("MACD", output1, 12, 26, 9, Type.Missing, Type.Missing);

      Indicore.IndicatorOutputCollectionAut _outputs = (Indicore.IndicatorOutputCollectionAut)instance2.Output;
      List<Indicore.IndicatorOutputAut> outputs = new List<Indicore.IndicatorOutputAut>();
      for (int i = 0; i < _outputs.Size; i++)
        outputs.Add((Indicore.IndicatorOutputAut)_outputs[i]);

      Indicore.IndicatorOutputAut output2 = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance2.Output)[0];
      
      instance1.Update(true);
      instance2.Update(true);
      for (int i = 0; i < rates.Length; i++) {
        yield return new IndicatorMACD(rates[i].StartDate, price(rates[i]), outputs[0][i], outputs[1][i], outputs[2][i]);
      }
    }
    public static void List() {
        Indicore.IndicoreManagerAut core;
      //get marketscope path
      string path = (string)Registry.GetValue("HKEY_LOCAL_MACHINE\\Software\\CandleWorks\\FXOrder2Go", "InstallPath", "");

      //initialize and load indicators.
      core = new Indicore.IndicoreManagerAut();
      object errors;

      if (!core.LoadFromCAB(path + "\\indicators\\indicators.cab", out errors)) {
        Console.WriteLine("Load standard: {0}", errors);
      }

      if (!core.LoadFromFolder(path + "\\indicators\\custom", out errors)) {
        Console.WriteLine("Load custom: {0}", errors);
      }

      Indicore.IndicatorCollectionAut indicators;
      indicators = (Indicore.IndicatorCollectionAut)core.Indicators;

      foreach (Indicore.IndicatorAut indicator in indicators) {
        string type, req;

        if (indicator.Type == indicator.TYPE_OSCILLATOR)
          type = "oscillator";
        else
          type = "indicator";

        if (indicator.RequiredSource == indicator.SOURCE_BAR)
          req = "bars";
        else
          req = "ticks";

        Console.WriteLine("{0} {1} ({2}) of {3}", type, indicator.ID, indicator.Name, req);

        Indicore.IndicatorParameterCollectionAut parameters = (Indicore.IndicatorParameterCollectionAut)indicator.CreateParameterSet();
        foreach (Indicore.IndicatorParameterAut parameter in parameters) {
          if (parameter.Type == parameter.TYPE_BOOL)
            type = "Bool";
          else if (parameter.Type == parameter.TYPE_INT)
            type = "Int";
          else if (parameter.Type == parameter.TYPE_DOUBLE)
            type = "Double";
          else if (parameter.Type == parameter.TYPE_STRING)
            type = "String";
          else if (parameter.Type == parameter.TYPE_COLOR)
            type = "Color";
          else
            type = "Unknown";

          Console.WriteLine("    {0} ({1}) : {2} = {3}", parameter.ID, parameter.Name, type, parameter.Default);
        }
      }
    }

    static void GetRSI(Rate[] rates) {

      //create the sample data
      Indicore.BarSourceAut source = CreateBarsData(rates);


      //find mva indicator
      Indicore.IndicatorCollectionAut indicators;
      indicators = (Indicore.IndicatorCollectionAut)core.Indicators;
      Indicore.IndicatorAut indicator = (Indicore.IndicatorAut)indicators["MVA"];
      //create parameter set and specify "N" parameter of the indicator.
      Indicore.IndicatorParameterCollectionAut parameters = (Indicore.IndicatorParameterCollectionAut)indicator.CreateParameterSet();
      ((Indicore.IndicatorParameterAut)(parameters["N"])).Value = 7;
      //create and instance of the indicator and force data update.
      Indicore.IndicatorInstanceAut instance = (Indicore.IndicatorInstanceAut)indicator.CreateIndicatorInstance(source, parameters);
      instance.Update(true);
      //get the indicator output (MVA has one output).
      Indicore.IndicatorOutputAut output = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance.Output)[0];

      //Print result.
      //Please note that the indicator output can be as longer as well as shorter than the indicator.
      //Also, the indicator results can start not from the first value of the source. The first defined value
      //of the indicator is in the output.FirstAvailable position.
      int max;
      if (source.Size > output.Size)
        max = source.Size;
      else
        max = output.Size;
      Console.WriteLine("{0}", instance.Name);
      Console.WriteLine("Date;Tick;MVA;");
      for (int i = 0; i < max; i++) {
        if (i < source.Size) {
          Indicore.TickAut tick = (Indicore.TickAut)source[i];
          Console.Write("{0};{1};", tick.Date, tick.Tick);
        } else
          Console.Write("n/a;n/a;");

        if (i >= output.FirstAvailable && i < output.Size)
          Console.Write("{0};", output[i]);
        else
          Console.Write("n/a;");

        Console.WriteLine();
      }
    }

    private static Indicore.BarSourceAut CreateBarsData(IList<Rate> rates ) {
      Indicore.BarSourceAut ticks = (Indicore.BarSourceAut)core.CreateBarSource("AAPL", "M1", false, 0.01);
      foreach (var rate in rates)
        ticks.AddLast(rate.StartDate, rate.PriceOpen, rate.PriceHigh, rate.PriceLow, rate.PriceClose, 1);
      return ticks;
    }
    private static Indicore.TickSourceAut CreateTicksData(IList<Rate> rates, Func<Rate, double> price) {
      Indicore.TickSourceAut ticks = (Indicore.TickSourceAut)core.CreateTickSource("XXX",.01);
      foreach (var r in rates) 
        ticks.AddLast(r.StartDate, price(r));
      return ticks;
    }
  }
}

