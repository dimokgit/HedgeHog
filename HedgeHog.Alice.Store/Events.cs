using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;

namespace HedgeHog.Alice.Store {
  public class BackTestEventArgs : EventArgs {
    public DateTime StartDate { get; set; }
    public int MonthToTest { get; set; }
    public double Delay { get; set; }
    public bool Pause { get; set; }
    public bool StepBack { get; set; }
    public bool ClearTest { get; set; }
    public BackTestEventArgs() { }
    public BackTestEventArgs( DateTime startDate, int monthsToTest, double delay, bool pause, bool clearTest) {
      this.StartDate = startDate;
      this.MonthToTest = monthsToTest;
      this.Delay = delay;
      this.Pause = pause;
      this.ClearTest = clearTest;
    }
  }
  public class MasterTradeEventArgs : EventArgs {
    public Trade MasterTrade { get; set; }
    public MasterTradeEventArgs(Trade trade) {
      this.MasterTrade = trade;
    }
  }

  public class TradingStatisticsEventArgs : EventArgs, INotifyPropertyChanged {
    private double _TakeProfitInPipsAverage;
    [DataMember]
    public double TakeProfitInPipsAverage {
      get { return _TakeProfitInPipsAverage; }
      set {
        if (_TakeProfitInPipsAverage != value) {
          _TakeProfitInPipsAverage = value;
          RaisePropertyChanged();
        }
      }
    }

    private double _VolumeRatioH;
    [DataMember]
    public double VolumeRatioH {
      get { return _VolumeRatioH; }
      set {
        if (_VolumeRatioH != value) {
          _VolumeRatioH = value;
          RaisePropertyChanged();
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
          RaisePropertyChanged();
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
          RaisePropertyChanged();
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
          RaisePropertyChanged();
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
          RaisePropertyChanged();
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
    protected virtual void RaisePropertyChanged() {
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
  }

}
