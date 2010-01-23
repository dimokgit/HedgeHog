using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
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
    public static IEnumerable<IndicatorPoint> RLW(FXW.Rate[] rates) { return RLW(rates, 14); }
    public static IEnumerable<IndicatorPoint> RLW(FXW.Rate[] rates, int period) {
      Indicore.BarSourceAut source = CreateBarsData(rates);
      Indicore.IndicatorInstanceAut instance = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("RLW", source, period, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance.Output)[0];
      instance.Update(true);
      return output.Cast<double>().Select((d, i) => new IndicatorPoint(rates[i].StartDate, d));
    }
    public static IEnumerable<IndicatorPoint> TSI(FXW.Rate[] rates) {
      Indicore.BarSourceAut source = CreateBarsData(rates);
      Indicore.IndicatorInstanceAut instance = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("TSI", source, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance.Output)[0];
      instance.Update(true);
      return output.Cast<double>().Select((d, i) => new IndicatorPoint(rates[i].StartDate, d));
    }
    public static void FillFractal(this FXW.Rate[] rates,Action<FXW.Rate,double?> setValue) {
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
    public static IEnumerable<FXW.Rate> HasFractal(this IEnumerable<FXW.Rate> rates, bool showDouble) {
      return rates.Where(r => r.Fractal.HasValue && (r.FractalBuy != 0 || r.FractalSell != 0));
    }
    public static IEnumerable<FXW.Rate> HasFractal(this IEnumerable<FXW.Rate> rates) {
      return rates.Where(r => r.Fractal.HasValue && r.Fractal != 0);
    }
    public static IEnumerable<FXW.Rate> HasFractal(this IEnumerable<FXW.Rate> rates, Func<FXW.Rate, bool> filter) {
      return rates.Where(filter).HasFractal();
      //foreach (var rate in rates.Where(filter))
      //  if (rate.Fractal.HasValue && rate.Fractal != 0) yield return rate;
    }
    public static FXW.Rate[] FillFractals(this FXW.Rate[] rates) {
      for (int i = 4; i < rates.Length; i++) {
        UpdateFractal(rates, i);
      }
      return rates;
    }
    static void UpdateFractal(FXW.Rate[] rates, int period) {
      if (period > 3) {
        var curr = rates[period - 2].BidHigh;
        if (curr > rates[period - 4].BidHigh && curr > rates[period - 3].BidHigh &&
            curr > rates[period - 1].BidHigh && curr > rates[period].BidHigh) {
          rates[period - 2].FractalSell = 1;
        } else
          rates[period - 2].FractalSell = 0;
        curr = rates[period - 2].AskLow;
        if (curr < rates[period - 4].AskLow && curr < rates[period - 3].AskLow &&
            curr < rates[period - 1].AskLow && curr < rates[period].AskLow)
          rates[period - 2].FractalBuy = -1;
        else
          rates[period - 2].FractalBuy = 0;
      }
    }
    public static FXW.Rate[] FillRsi(this FXW.Rate[] rates, int period, Func<FXW.Rate, double> getPrice) {
      for (int i = period; i < rates.Length; i++) {
        UpdateRsi(period, rates, i, getPrice);
      }
      return rates;
    }
    static void UpdateRsi(int numberOfPeriods, FXW.Rate[] rates, int period,Func<FXW.Rate,double> getPrice){
      if (period >= numberOfPeriods) {
        var i = 0;
        var sump = 0.0;
        var sumn = 0.0;
        var positive = 0.0;
        var negative = 0.0;
        var diff = 0.0;
        if (period == numberOfPeriods) {
          for (i = period - numberOfPeriods + 1; i <= period; i++) {
            diff = getPrice(rates[i]) - getPrice(rates[i - 1]);
            if (diff >= 0)
              sump = sump + diff;
            else
              sumn = sumn - diff;
          }
          positive = sump / numberOfPeriods;
          negative = sumn / numberOfPeriods;
        } else {
          diff = getPrice(rates[period]) - getPrice(rates[period - 1]);
          if (diff > 0)
            sump = diff;
          else
            sumn = -diff;
          positive = (rates[period - 1].PriceRsiP * (numberOfPeriods - 1) + sump) / numberOfPeriods;
          negative = (rates[period - 1].PriceRsiN * (numberOfPeriods - 1) + sumn) / numberOfPeriods;
        }
        rates[period].PriceRsiP = positive;
        rates[period].PriceRsiN = negative;
        if (negative == 0)
          rates[period].PriceRsi = 0;
        else
          rates[period].PriceRsi = 100 - (100 / (1 + positive / negative));
      }
    }

    public static IEnumerable<TBars> FillFractals<TBars>(this IEnumerable<TBars> rates, TimeSpan period) where TBars : FXW.Rate {
      DateTime nextDate = rates.First().StartDate;
      foreach (var rate in rates.Where(r => r.StartDate.Between(rates.First().StartDate + period, rates.Last().StartDate - period) && !r.Fractal.HasValue).ToArray()) {
          UpdateFractal(rates, rate, period);
      }
      return rates;
    }
    static void UpdateFractal<TBars>(IEnumerable<TBars> rates, TBars rate, TimeSpan period) where TBars : FXW.Rate {
      var ratesInRange = rates.Where(r => r.StartDate.Between(rate.StartDate - period, rate.StartDate + period)).ToArray();
      rate.FractalSell = ratesInRange.Max(r => r.BidHigh) == rate.BidHigh ? 1 : 0;
      rate.FractalBuy = ratesInRange.Min(r => r.AskLow) == rate.AskLow ? -1 : 0;
    }


    public static IEnumerable<IndicatorPoint2> TSI_CR(FXW.Rate[] rates) {
      Indicore.BarSourceAut source = CreateBarsData(rates);
      Indicore.IndicatorInstanceAut instance1 = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("TSI", source, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output1 = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance1.Output)[0];
      Indicore.IndicatorInstanceAut instance2 = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("CR", output1, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output2 = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance2.Output)[0];
      instance1.Update(true);
      instance2.Update(true);
      return output2.Cast<double>().Select((d, i) => new IndicatorPoint2(rates[i].StartDate, output1[i], d));
    }
    public static IEnumerable<IndicatorPoint> RSI(FXW.Rate[] rates, Func<FXW.Rate, double> price, int period) {
      Indicore.TickSourceAut source = CreateTicksData(rates, price);
      Indicore.IndicatorInstanceAut instance = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("RSI", source, period, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance.Output)[0];
      instance.Update(true);
      return output.Cast<double>().Select((d, i) => new IndicatorPoint(rates[i].StartDate, d));
    }
    public static void CR<T>(this FXW.Rate[] rates, Func<FXW.Rate, double> sourceLambda,Action<FXW.Rate,T>destinationLambda) {
      Indicore.TickSourceAut source = CreateTicksData(rates, sourceLambda);
      Indicore.IndicatorInstanceAut instance = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("CR", source, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance.Output)[0];
      instance.Update(true);
      var i = 0;
      output.Cast<T>().ToList().ForEach(d => destinationLambda(rates[i++], d));
    }
    public static IEnumerable<IndicatorPoint2> RSI_CR(FXW.Rate[] rates, Func<FXW.Rate, double> price, int period) {
      Indicore.TickSourceAut source = CreateTicksData(rates, price);
      Indicore.IndicatorInstanceAut instance1 = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("RSI", source, period, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output1 = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance1.Output)[0];
      Indicore.IndicatorInstanceAut instance2 = (Indicore.IndicatorInstanceAut)core.CreateIndicatorInstance("CR", output1, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
      Indicore.IndicatorOutputAut output2 = (Indicore.IndicatorOutputAut)((Indicore.IndicatorOutputCollectionAut)instance2.Output)[0];
      instance1.Update(true);
      instance2.Update(true);
      return output2.Cast<double>().Select((d, i) => new IndicatorPoint2(rates[i].StartDate, output1[i], d));
    }
    public static IEnumerable<IndicatorMACD> RsiMACD(FXW.Rate[] rates, Func<FXW.Rate, double> price, int period) {
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

    static void GetRSI(FXW.Rate[] rates) {

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

    private static Indicore.BarSourceAut CreateBarsData(FXW.Rate[] rates ) {
      Indicore.BarSourceAut ticks = (Indicore.BarSourceAut)core.CreateBarSource("AAPL", "M1", false, 0.01);
      foreach (var rate in rates)
        ticks.AddLast(rate.StartDate, rate.PriceOpen, rate.PriceHigh, rate.PriceLow, rate.PriceClose, 1);
      return ticks;
    }
    private static Indicore.TickSourceAut CreateTicksData(FXW.Rate[] rates, Func<FXW.Rate, double> price) {
      Indicore.TickSourceAut ticks = (Indicore.TickSourceAut)core.CreateTickSource("XXX",.01);
      foreach (var r in rates) 
        ticks.AddLast(r.StartDate, price(r));
      return ticks;
    }
  }
}

