using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog;
using HedgeHog.Bars;

namespace HedgeHog.Alice.Server {
  public class PairInfo<TBar> : GalaSoft.MvvmLight.ViewModelBase where TBar:Bars.BarBase {
    #region Events
    public event EventHandler ReLoadRates;
    protected void RaiseReLoadRates(){
      if(ReLoadRates!=null)
        ReLoadRates(this,EventArgs.Empty);
    }
    #endregion

    #region Members
    #region Rates
    private List<TBar> _Rates;
    public List<TBar> Rates {
      get { return _Rates; }
      set {
        if (_Rates != value) {
          _Rates = value;
          RaisePropertyChanged("Rates");
        }
      }
    }

    #endregion
    #region BidHighToAskLowRatio
    private double _BidHighToAskLowRatio;
    public double BidHighToAskLowRatio {
      get { return _BidHighToAskLowRatio; }
      set {
        if (_BidHighToAskLowRatio != value) {
          _BidHighToAskLowRatio = value;
          RaisePropertyChanged(() => BidHighToAskLowRatio);
        }
      }
    }

    #endregion
    #region BidHighToAskLowRatioMA
    private double _BidHighToAskLowRatioMA;
    public double BidHighToAskLowRatioMA {
      get { return _BidHighToAskLowRatioMA; }
      set {
        if (_BidHighToAskLowRatioMA != value) {
          _BidHighToAskLowRatioMA = value;
          RaisePropertyChanged(() => BidHighToAskLowRatioMA);
        }
      }
    }

    #endregion
    #region StatsFramePeriods
    private int _StatsFramePeriods = -1;
    public int StatsFramePeriods {
      get { return _StatsFramePeriods > 0 ? _StatsFramePeriods : Rates.Count / 10; }
      set {
        if (_StatsFramePeriods != value) {
          _StatsFramePeriods = value;
          RaisePropertyChanged("StatsFramePeriods");
          UpdateStatistics();
        }
      }
    }

    #endregion
    #region Period
    private int _Period = -1;
    public int Period {
      get { return _Period; }
      set {
        if (_Period != value) {
          _Period = value;
          RaisePropertyChanged("Period");
          RaiseReLoadRates();
        }
      }
    }

    #endregion
    #region Periods
    private int _Periods = -1;
    public int Periods {
      get { return _Periods; }
      set {
        if (_Periods != value) {
          _Periods = value;
          RaisePropertyChanged("Periods");
          RaiseReLoadRates();
        }
      }
    }

    #endregion
    public string Pair { get; set; }
    private DateTime _LastDate;
    public DateTime LastDate {
      get { return Rates.Select(r => r.StartDate).LastOrDefault(); }
    }
    private int _Count;
    public int Count {
      get { return Rates.Count; }
    }
    private int _PullsCount;
    public int PullsCount {
      get { return _PullsCount; }
      set {
        if (_PullsCount != value) {
          _PullsCount = value;
          RaisePropertyChanged("PullsCount");
        }
      }
    }


    #endregion

    #region Ctor
    public PairInfo(string pair,Func<double,double> inPips) {
      this.Pair = pair;
      this.Rates = new List<TBar>();
      this.InPips = inPips;
    }
    #endregion

    #region Methods
    public void UpdateStatistics() {
      Rates.Sort((x, y) => x.StartDate.CompareTo(y.StartDate));
      RaisePropertyChanged("Count");
      RaisePropertyChanged("LastDate");
      Rates.SetBidHighToAskLowRatioMA(StatsFramePeriods);
      this.BidHighToAskLowRatioMA = this.InPips(Rates.Select(r => r.BidHighAskLowDifferenceMA).Where(ma => !double.IsNaN(ma)).ToList().AverageByIterations(2).Average());
      this.BidHighToAskLowRatio = this.InPips(Rates.Select(r => r.BidHighAskLowDiference).ToList().AverageByIterations(2).Average());
    }
    #endregion


    public Func<double, double> InPips { get; set; }
  }
}
