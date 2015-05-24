using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace HedgeHog.Alice.Store {
  public class TradingStatistics : INotifyPropertyChanged{

    Func<double> _GetNetInPips;
    public Func<double> GetNetInPips {
      get { return _GetNetInPips; }
      set { _GetNetInPips = value; }
    }

    #region CurrentGrossInPips
    private double _CurrentGrossInPips;
    public double CurrentGrossInPips {
      get { return _CurrentGrossInPips; }
      set {
        if (_CurrentGrossInPips != value) {
          _CurrentGrossInPips = value;
          RaisePropertyChanged("CurrentGrossInPips");
        }
      }
    }
    #endregion

    #region CurrentLoss
    private double _CurrentGross = double.NaN;
    public double CurrentGross {
      get { return _CurrentGross; }
      set {
        if (_CurrentGross != value) {
          _CurrentGross = value;
          RaisePropertyChanged("CurrentGross");
        }
      }
    }

    #endregion

    #region CorridorHeightToRatesHeightRatio
    private double _AllowedLotMinimum;
    public double AllowedLotMinimum {
      get { return _AllowedLotMinimum; }
      set {
        if (_AllowedLotMinimum != value) {
          _AllowedLotMinimum = value;
          RaisePropertyChanged("AllowedLotMinimum");
        }
      }
    }

    #endregion

    #region StDev
    private double _StDevPips = double.NaN;
    public double StDevPips {
      get { return _StDevPips; }
      set {
        if (_StDevPips != value) {
          _StDevPips = value;
          RaisePropertyChanged("StDevPips");
        }
      }
    }

    #endregion
    #region TakeProfitPips
    private double _TakeProfitPips = double.NaN;
    public double TakeProfitPips {
      get { return _TakeProfitPips; }
      set {
        if (_TakeProfitPips != value) {
          _TakeProfitPips = value;
          RaisePropertyChanged("TakeProfitPips");
        }
      }
    }

    #endregion
    private double _TakeProfitDistanceInPips = double.NaN;
    [DataMember]
    public double TakeProfitDistanceInPips {
      get { return _TakeProfitDistanceInPips; }
      set {
        if (_TakeProfitDistanceInPips != value) {
          _TakeProfitDistanceInPips = value;
          RaisePropertyChanged("TakeProfitDistanceInPips");
        }
      }
    }

    #region GrossToExitInPips
    private double? _GrossToExitInPips;
    public double? GrossToExitInPips {
      get { return _GrossToExitInPips; }
      set {
        if (_GrossToExitInPips != value) {
          _GrossToExitInPips = value;
          RaisePropertyChanged("GrossToExitInPips");
        }
      }
    }

    #endregion


    private double _VolumeRatioH;
    [DataMember]
    public double VolumeRatioH {
      get { return _VolumeRatioH; }
      set {
        if (_VolumeRatioH != value) {
          _VolumeRatioH = value;
          RaisePropertyChanged("VolumeRatioH");
        }
      }
    }

    #region VolumeRatioL
    private double _VolumeRatioL;
    public double VolumeRatioL {
      get { return _VolumeRatioL; }
      set {
        if (_VolumeRatioL != value) {
          _VolumeRatioL = value;
          RaisePropertyChanged("VolumeRatioL");
        }
      }
    }

    #endregion

    private double _RatesStDevToRatesHeightRatioH;
    [DataMember]
    public double RatesStDevToRatesHeightRatioH {
      get { return _RatesStDevToRatesHeightRatioH; }
      set {
        if (_RatesStDevToRatesHeightRatioH != value) {
          _RatesStDevToRatesHeightRatioH = value;
          RaisePropertyChanged("RatesStDevToRatesHeightRatioH");
        }
      }
    }

    #region RatesStDevToRatesHeightRatioL
    private double _RatesStDevToRatesHeightRatioL;
    public double RatesStDevToRatesHeightRatioL {
      get { return _RatesStDevToRatesHeightRatioL; }
      set {
        if (_RatesStDevToRatesHeightRatioL != value) {
          _RatesStDevToRatesHeightRatioL = value;
          RaisePropertyChanged("RatesStDevToRatesHeightRatioL");
        }
      }
    }

    #endregion
    private double _RateStDevAverage;
    [DataMember]
    public double RateStDevAverage {
      get { return _RateStDevAverage; }
      set {
        if (_RateStDevAverage != value) {
          _RateStDevAverage = value;
          RaisePropertyChanged("RateStDevAverage");
        }
      }
    }
    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>
    /// Provides access to the PropertyChanged event handler to derived classes.
    /// </summary>
    protected PropertyChangedEventHandler PropertyChangedHandler {
      get {
        return PropertyChanged;
      }
    }

    /// <summary>
    /// Verifies that a property name exists in this ViewModel. This method
    /// can be called before the property is used, for instance before
    /// calling RaisePropertyChanged. It avoids errors when a property name
    /// is changed but some places are missed.
    /// <para>This method is only active in DEBUG mode.</para>
    /// </summary>
    /// <param name="propertyName"></param>
    [Conditional("DEBUG")]
    [DebuggerStepThrough]
    public void VerifyPropertyName(string propertyName) {
      var myType = this.GetType();
      if (myType.GetProperty(propertyName) == null) {
        throw new ArgumentException("Property not found", propertyName);
      }
    }

    /// <summary>
    /// Raises the PropertyChanged event if needed.
    /// </summary>
    /// <remarks>If the propertyName parameter
    /// does not correspond to an existing property on the current class, an
    /// exception is thrown in DEBUG configuration only.</remarks>
    /// <param name="propertyName">The name of the property that
    /// changed.</param>
    [SuppressMessage("Microsoft.Design", "CA1030:UseEventsWhereAppropriate",
        Justification = "This cannot be an event")]
    protected virtual void RaisePropertyChanged(string propertyName) {
      VerifyPropertyName(propertyName);

      var handler = PropertyChanged;

      if (handler != null) {
        handler(this, new PropertyChangedEventArgs(propertyName));
      }
    }

    /// <summary>
    /// Raises the PropertyChanged event if needed.
    /// </summary>
    /// <typeparam name="T">The type of the property that
    /// changed.</typeparam>
    /// <param name="propertyExpression">An expression identifying the property
    /// that changed.</param>
    [SuppressMessage("Microsoft.Design", "CA1030:UseEventsWhereAppropriate",
        Justification = "This cannot be an event")]
    [SuppressMessage(
        "Microsoft.Design",
        "CA1006:GenericMethodsShouldProvideTypeParameter",
        Justification = "This syntax is more convenient than other alternatives.")]
    protected virtual void RaisePropertyChanged<T>(Expression<Func<T>> propertyExpression) {
      if (propertyExpression == null) {
        return;
      }

      var handler = PropertyChanged;

      if (handler != null) {
        var body = propertyExpression.Body as MemberExpression;
        var expression = body.Expression as ConstantExpression;
        handler(expression.Value, new PropertyChangedEventArgs(body.Member.Name));
      }
    }

    /// <summary>
    /// When called in a property setter, raises the PropertyChanged event for 
    /// the current property.
    /// </summary>
    /// <exception cref="InvalidOperationException">If this method is called outside
    /// of a property setter.</exception>
    [SuppressMessage("Microsoft.Design", "CA1030:UseEventsWhereAppropriate",
        Justification = "This cannot be an event")]
    protected virtual void RaisePropertyChanged_() {
      var frames = new StackTrace();

      for (var i = 0; i < frames.FrameCount; i++) {
        var frame = frames.GetFrame(i).GetMethod() as MethodInfo;
        if (frame != null)
          if (frame.IsSpecialName && frame.Name.StartsWith("set_", StringComparison.OrdinalIgnoreCase)) {
            RaisePropertyChanged(frame.Name.Substring(4));
            return;
          }
      }

      throw new InvalidOperationException("This method can only by invoked within a property setter.");
    }

    public double CurrentLossInPips { get; set; }

    IList<TradingMacro> _TradingMacros = new TradingMacro[0];
    public IList<TradingMacro> TradingMacros {
      get { return _TradingMacros; }
      set { _TradingMacros = value; }
    }
    IList<TradingMacro> _TradingMacrosActive = new TradingMacro[0];
    public IList<TradingMacro> TradingMacrosActive {
      get { return _TradingMacrosActive; }
      set { _TradingMacrosActive = value; }
    }

    public double CurrentLoss { get; set; }
    public double OriginalProfit { get; set; }
  }
}
