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
      var FftReversed = false;

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
    private ConcurrentQueue<Harmonic> CalcHurmonics(IList<Rate> binRates,int minutesPerHour) {
      ConcurrentDictionary<int, double[]> invFfts;
      ConcurrentQueue<Harmonic> harmonicsQueue;
      CalcHurmonics(binRates,minutesPerHour, out harmonicsQueue, out invFfts);
      return harmonicsQueue;
    }
    private void CalcHurmonics(IList<Rate> binRates, out ConcurrentQueue<Harmonic> harmonicsQueue, out ConcurrentDictionary<int, double[]> invFfts) {
      CalcHurmonics(binRates, 60,out  harmonicsQueue, out invFfts);
    }
    private void CalcHurmonics(IList<Rate> binRates,int minutesPerHour, out ConcurrentQueue<Harmonic> harmonicsQueue, out ConcurrentDictionary<int, double[]> invFfts) {
      var bins = binRates.Select(_priceAvg).FftSignalBins(false);
      harmonicsQueue = new Harmonic[0].ToConcurrentQueue();
      var iffts = invFfts = "".ToConcurrentDictionary(a => 0, a => new double[0]);
      var harmonicHours = ParallelEnumerable.Range(1, binRates.Count).GroupBy(i => ((double)binRates.Count / i / minutesPerHour).ToInt()).Where(g => g.Key >= 1);
      var hq = harmonicsQueue;
      harmonicHours.ForAll(group => {
        var hour = group.Key;
        var bins1 = bins.FftHarmonic(group.Min(), group.Count());
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
      var bins = binRates.Select(_priceAvg).FftBins();
      var iffts = invFfts = "".ToConcurrentDictionary(a => 0, a => new double[0]);
      var harmonicHours = ParallelEnumerable.Range(1, binRates.Count).Where(i => binRates.Count / i >= minutesMin );
      var hq = new ConcurrentQueue<Harmonic>();
      harmonicHours.ForAll(minute => {
        var hour =((double)binRates.Count / minute).ToInt();
        var bins1 = bins.FftHarmonic(minute);
        double[] ifft;
        alglib.fftr1dinv(bins1.SafeArray(), out ifft);
        var height = ifft.Height();
        hq.Enqueue(new Harmonic(hour, InPips(height).Round(1)));
        iffts.TryAdd(hour, ifft);
      });
      return hq.ToArray();
    }    
  }
}
