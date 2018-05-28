using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Linq.Expressions;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace HedgeHog.Shared {
  [Serializable]
  [DataContract]
  public abstract class PositionBase :INotifyPropertyChanged {
    [Key]
    [DataMember]
    public string Id { get; set; }
    private DateTime _time;
    [DataMember]
    [DisplayFormat(DataFormatString = "{0:dd HH:mm}")]
    public virtual DateTime Time {
      get => _time;
      set {
        _time = value;
        Id = value.ToEpochTime() + "";
      }
    }
    public enum PositionKind { Unknown, Open, Closed };
    [DataMember]
    public PositionKind Kind { get; set; }
    [DataMember]
    public string KindString { get { return Kind + ""; } }
    double _PipSize;
    [UpdateOnUpdate]
    public double PipSize {
      get { return _PipSize; }
      set {
        _PipSize = value;
      }
    }
    [DataMember]
    [UpdateOnUpdate]
    public double PipCost { get; set; }

    [DataMember]
    [UpdateOnUpdate]
    /// <summary>
    /// 2,4
    /// </summary>
    public double PointSize { get; set; }
    public string PointSizeFormat { get { return "n" + PointSize; } }

    private double _StopAmount;

    [UpdateOnUpdate]
    [DataMember]
    public double StopAmount {
      get { return _StopAmount; }
      set {
        if(_StopAmount != value) {
          _StopAmount = value;
          OnPropertyChanged("StopAmount");
        }
      }
    }

    [UpdateOnUpdate]
    [DataMember]
    public double LimitAmount { get; set; }

    public UnKnownBase UnKnown { get; set; }

    public TU InitUnKnown<TU>() where TU : UnKnownBase, new() {
      if(UnKnown == null) UnKnown = new TU();
      return UnKnown as TU;
    }

    public void Update(PositionBase trade, params Action<object>[] setUnKnown) {
      var props = new List<string>();
      var propsToUpdate = GetType().GetProperties().Where(p => p.GetCustomAttributes(typeof(UpdateOnUpdateAttribute), true).Count() > 0).ToArray();
      foreach(var prop in propsToUpdate) {
        prop.SetValue(this, prop.GetValue(trade, null), null);
        props.Add(prop.Name);
        var names = (prop.GetCustomAttributes(typeof(UpdateOnUpdateAttribute), true).First() as UpdateOnUpdateAttribute).Names;
        if(names != null) props.AddRange(names);
      }
      OnPropertyChanged(props.ToArray());
      foreach(var su in setUnKnown)
        su(UnKnown);
      if(setUnKnown.Length > 0) OnPropertyChanged("UnKnown");
      //this.Close = trade.Close;
      //this.GrossPL = trade.GrossPL;
      //this.Limit = trade.Limit;
      //this.LimitAmount = trade.LimitAmount;
      //this.PL = trade.PL;
      //this.Stop = trade.Stop;
      //this.StopAmount = trade.StopAmount;
      //OnPropertyChanged("Close", "GrossPL", "Limit", "LimitAmount", "PL", "Stop", "StopAmount");
    }


    #region INotifyPropertyChanged Members

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(params string[] propertyNames) {
      foreach(var pn in propertyNames)
        OnPropertyChanged(pn);
    }
    protected virtual void OnPropertyChanged(string propertyName) {
      if(PropertyChanged == null) return;
      PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
  }

  public abstract class UnKnownBase :INotifyPropertyChanged {
    #region INotifyPropertyChanged Members
    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string pn) {
      if(PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(pn));
    }
    #endregion
  }

  public class UpdateOnUpdateAttribute :Attribute {
    public string[] Names { get; set; }
    public UpdateOnUpdateAttribute(params string[] names) {
      this.Names = names;
    }
    public UpdateOnUpdateAttribute() {

    }
  }
}
