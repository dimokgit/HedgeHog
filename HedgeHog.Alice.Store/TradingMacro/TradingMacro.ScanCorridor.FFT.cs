using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Models;
using HedgeHog.Bars;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Collections.Concurrent;

namespace HedgeHog.Alice.Store {
  class Harmonic : Models.ModelBase {
    #region Hours
    private double _Hours;
    [Display(Name = "Hrs")]
    public double Hours {
      get { return _Hours; }
      set {
        if (_Hours != value) {
          _Hours = value;
          RaisePropertyChanged("Minutes");
          RaisePropertyChanged("Hours");
        }
      }
    }
    #endregion
    #region Height
    private double _Height;
    [Display(Name = "Hght")]
    public double Height {
      get { return _Height; }
      set {
        if (_Height != value) {
          _Height = value;
          RaisePropertyChanged("Height");
        }
      }
    }

    #endregion
    #region InRange
    private bool _InRange;
    [Display(AutoGenerateField = false)]
    public bool InRange {
      get { return _InRange; }
      set {
        if (_InRange != value) {
          _InRange = value;
          RaisePropertyChanged("InRange");
          RaisePropertyChanged("Color");
        }
      }
    }
    #endregion
    #region IsAggregate
    private bool _IsAggregate;
    [Display(AutoGenerateField = false)]
    public bool IsAggregate {
      get { return _IsAggregate; }
      set {
        if (_IsAggregate != value) {
          _IsAggregate = value;
          RaisePropertyChanged("IsAggregate");
          RaisePropertyChanged("FontWeight");
        }
      }
    }
    #endregion
    string _Color = Colors.PowderBlue.ToString().Replace("#FF", "#52");
    [Display(AutoGenerateField = false)]
    public string Color {
      get { return InRange ? _Color : ""; }
    }
    [Display(AutoGenerateField = false)]
    public string FontWeight {
      get { return IsAggregate ? "Bold" : "Normal"; }
    }
    public Harmonic() { }
    public Harmonic(double hours, double height) {
      this.Hours = hours;
      this.Height = height;
    }

    public override string ToString() {
      return new { Hours, Height } + "";
    }
  }

  public partial class TradingMacro {

    private CorridorStatistics ScanCorridorByFft(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot().ToArray();

      FttMax = ratesReversed.Select(_priceAvg).FftFrequency(FftReversed).ToInt();

      var startMax = CorridorStopDate.IfMin(DateTime.MaxValue);
      var startMin = CorridorStartDate.GetValueOrDefault(ratesReversed[CorridorDistanceRatio.ToInt() - 1].StartDate);
      Lazy<int> lenghForwardOnly = new Lazy<int>(() => {
        var date = CorridorStats.Rates.Last().StartDate;
        return ratesReversed.TakeWhile(r => r.StartDate >= date).Count();
      });
      var lengthMax = !IsCorridorForwardOnly || CorridorStats.StartDate.IsMin() ? int.MaxValue : lenghForwardOnly.Value;
      WaveShort.Rates = null;
      WaveShort.Rates = startMax.IsMax() && !CorridorStartDate.HasValue
        ? ratesReversed.Take(FttMax.Min(lengthMax)).ToArray()
        : ratesReversed.SkipWhile(r => r.StartDate > startMax).TakeWhile(r => r.StartDate >= startMin).ToArray();
      var corridor = WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);

      SetVoltage(ratesReversed[0], WaveShort.Rates.Select(_priceAvg).FftFrequency(FftReversed) / (double)WaveShort.Rates.Count);
      var volts = RatesArray.Select(r => GetVoltage(r)).SkipWhile(v => v.IsNaN()).ToArray();
      double voltsAvg;
      var voltsStdev = volts.StDev(out voltsAvg);
      GetVoltageAverage = () => voltsAvg;
      GetVoltageHigh = () => voltsAvg + voltsStdev;
      return corridor;
    }


    IList<Harmonic> _harmonics;
    ValueTrigger<bool> _canTradeByHarmonicsTrigger = new ValueTrigger<bool>(false);
    bool CanTradeByHarmonics() {
      return HarmonicsAverage * 2 < _harmonics[0].Height;
    }

    private CorridorStatistics ScanCorridorByHorizontalLineCrosses3(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {

      var binRates = ratesForCorridor;
      System.Collections.Concurrent.ConcurrentQueue<Harmonic> harmonicsQueue;
      System.Collections.Concurrent.ConcurrentDictionary<int, double[]> iffts;
      CalcHurmonics(binRates, out harmonicsQueue, out iffts);
      var harmonics = harmonicsQueue.OrderByDescending(w => w.Height).ToList();
      var xxx1 = harmonics.AverageByIterations(w => w.Height, (w, v) => w >= v, PolyOrder).ToArray();
      xxx1.ForEach(w => { w.InRange = true; });
      var harmonicMain = harmonics.Single(h => h.Hours == DistanceIterations);
      //.AverageByIterations(w => w.Hei2ght, (w, v) => w >= v, 2)
      //.OrderBy(w => w.Hours).First();
      harmonicMain.IsAggregate = true;
      ParallelEnumerable.Range(0, binRates.Count).ForAll(i => SetVoltage(binRates[i], InPips(iffts[harmonicMain.Hours.ToInt()][i])));
      var avgHour = harmonics.Sum(h => h.Hours * h.Height) / harmonics.Sum(h => h.Height);
      var avgHeight = harmonics.Sum(h => h.Hours * h.Height) / harmonics.Sum(h => h.Hours);
      var harmonicAvg = harmonics.OrderBy(h => (h.Hours - avgHour).Abs()).First();
      CorridorStDevRatioMax = avgHour * 60.0;
      _harmonics = harmonics.ToArray();
      var averageHour = avgHour;// harmonics.TakeWhile(w => w.InRange).Average(w => w.Hours);
      HarmonicsAverage = avgHeight;// harmonicAvg.Height;// harmonics.TakeWhile(w => w.InRange).Average(w => w.Height);
      harmonics.Add(new Harmonic(averageHour.Round(1), HarmonicsAverage.Round(1)));
      GlobalStorage.Instance.ResetGenericList(harmonics);

      double level = double.NaN;
      var rates = !CanTradeByHarmonics() ? null
        : CorridorByVerticalLineCrosses2(ratesForCorridor.ReverseIfNot(), _priceAvg, CorridorDistanceRatio.ToInt(), out level);
      var corridorOk = rates != null && rates.Any() && (!IsCorridorForwardOnly || rates.LastBC().StartDate >= CorridorStats.StartDate)
        && (IsCorridorForwardOnly || MagnetPrice.IfNaN(0).Abs(level) > TradingDistance);
      if (corridorOk) {
        MagnetPrice = level;
        WaveShort.ResetRates(rates);
      } else if (CorridorStats.Rates != null && CorridorStats.Rates.Any()) {
        var dateStop = CorridorStats.Rates.LastBC().StartDate;
        WaveShort.ResetRates(ratesForCorridor.ReverseIfNot().TakeWhile(r => r.StartDate >= dateStop).ToArray());
      } else
        WaveShort.ResetRates(ratesForCorridor.ReverseIfNot());

      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

    private void CalcHurmonics(IList<Rate> binRates, out ConcurrentQueue<Harmonic> harmonicsQueue, out ConcurrentDictionary<int, double[]> invFfts) {
      var bins = binRates.Select(_priceAvg).FftSignalBins(false);
      harmonicsQueue = new Harmonic[0].ToConcurrentQueue();
      var iffts = invFfts = "".ToConcurrentDictionary(a => 0, a => new double[0]);
      var harmonicHours = ParallelEnumerable.Range(1, binRates.Count).GroupBy(i => (binRates.Count / i / 60.0).ToInt()).Where(g => g.Key >= 1);
      var hq = harmonicsQueue;
      harmonicHours.ForAll(group => {
        var hour = group.Key;
        var bins1 = bins.FftHarmonic(group.First(), group.Count());
        double[] ifft;
        alglib.fftr1dinv(bins1.SafeArray(), out ifft);
        var height = ifft.Where(d => d > 0).Average();
        hq.Enqueue(new Harmonic(hour, InPips(height).Round(1)));
        iffts.TryAdd(hour, ifft);
      });
    }
    private IList<Harmonic> CalcHurmonicsAll(IList<Rate> binRates,int minutesMin) {
      ConcurrentDictionary<int, double[]> invFfts;
      return CalcHurmonicsAll(binRates, minutesMin, out invFfts);
    }
    private IList<Harmonic> CalcHurmonicsAll(IList<Rate> binRates, int minutesMin, out ConcurrentDictionary<int, double[]> invFfts) {
      var bins = binRates.Select(_priceAvg).FftSignalBins(false);
      var iffts = invFfts = "".ToConcurrentDictionary(a => 0, a => new double[0]);
      var harmonicHours = ParallelEnumerable.Range(1, binRates.Count).Where(i => binRates.Count / i >= minutesMin );
      var hq = new ConcurrentQueue<Harmonic>();
      harmonicHours.ForAll(minute => {
        var hour =((double)binRates.Count / minute).ToInt();
        var bins1 = bins.FftHarmonic(minute);
        double[] ifft;
        alglib.fftr1dinv(bins1.SafeArray(), out ifft);
        var height = ifft.Max();
        hq.Enqueue(new Harmonic(hour, InPips(height).Round(1)));
        iffts.TryAdd(hour, ifft);
      });
      return hq.ToArray();
    }

    private alglib.complex[] CalcFftStats(IList<Rate> corridorRates, int ifftSkpip) {
      alglib.complex[] bins;
      corridorRates.Select(_priceAvg).FftFrequency(false, out bins);
      return bins;
      Func<int, IEnumerable<alglib.complex>> repeat = (count) => { return Enumerable.Repeat(new alglib.complex(0), count); };
      //alglib.fftr1dinv(bins.Take(ifftSkpip).Concat(Enumerable.Repeat(new alglib.complex(0), bins.Length - ifftSkpip)).ToArray(), out ifft);
      //var bins1 = new[] { bins.Take(1), NewMethod(ifftSkpip - 1), new[] { bins[ifftSkpip] }, NewMethod(bins.Length - ifftSkpip - 1) }
      //  .SelectMany(b => b).ToArray();
      var bins1 = new[] { bins.Take(1), repeat(ifftSkpip - 1), bins.Skip(ifftSkpip) }.SelectMany(b => b).ToArray();
      double[] ifft;
      alglib.fftr1dinv(bins1, out ifft);
      //Enumerable.Range(0, corridorRates.Count).ForEach(i => SetVoltage(corridorRates[i], InPips(ifft[i])));
      SetVoltage(RateLast, InPips(ifft.Select(v => v.Abs()).ToArray().AverageByIterations(-1).Average()));
      var voltsAvg = 0.0;
      var voltsStDev = RatesArray.Select(GetVoltage).SkipWhile(v => v.IsNaN()).Where(v => !v.IsNaN()).ToArray().StDev(out voltsAvg);
      GetVoltageAverage = () => voltsAvg;
      GetVoltageHigh = () => voltsAvg - voltsStDev * 2;
      return bins;
    }
    
  }
}
