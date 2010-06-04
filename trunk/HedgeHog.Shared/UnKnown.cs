using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Linq.Expressions;

namespace HedgeHog.Shared {
  [Serializable]
  [DataContract]
  public abstract class PositioBase : INotifyPropertyChanged {

    [UpdateOnUpdate]
    [DataMember]
    public double StopAmount { get; set; }
    [UpdateOnUpdate]
    [DataMember]
    public double LimitAmount { get; set; }

    public UnKnownBase UnKnown { get; set; }

    public TU InitUnKnown<TU>() where TU : UnKnownBase,new() {
      if (UnKnown == null) UnKnown = new TU();
      return UnKnown as TU;
    }

    public void Update(Trade trade,params Action<object>[] setUnKnown) {
      var props = new List<string>();
      var propsToUpdate = GetType().GetProperties().Where(p => p.GetCustomAttributes(typeof(UpdateOnUpdateAttribute), true).Count()>0).ToArray();
      foreach (var prop in propsToUpdate) {
        prop.SetValue(this, prop.GetValue(trade, null), null);
        props.Add(prop.Name);
      }
      OnPropertyChanged(props.ToArray());
      foreach (var su in setUnKnown)
        su(UnKnown);
      if (setUnKnown.Length > 0) OnPropertyChanged("UnKnown");
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
      foreach (var pn in propertyNames)
        OnPropertyChanged(pn);
    }
    protected virtual void OnPropertyChanged(string propertyName) {
      if (PropertyChanged == null) return;
      PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
  }

  public abstract class UnKnownBase : INotifyPropertyChanged {
    #region INotifyPropertyChanged Members
    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string pn) {
      if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(pn));
    }
    #endregion
  }

  public class UpdateOnUpdateAttribute : Attribute {}
}
