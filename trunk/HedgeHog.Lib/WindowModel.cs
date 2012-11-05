using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;

namespace HedgeHog.Models {
  public class ModelBase : INotifyPropertyChanged {
    #region INotifyPropertyChanged Members

    #region PropertyChanged Event
    event PropertyChangedEventHandler PropertyChangedEvent;
    public event PropertyChangedEventHandler PropertyChanged {
      add {
        if (PropertyChangedEvent == null || !PropertyChangedEvent.GetInvocationList().Contains(value))
          PropertyChangedEvent += value;
      }
      remove {
        PropertyChangedEvent -= value;
      }
    }
    #endregion


    protected void RaisePropertyChanged(params Expression<Func<object>>[] propertyLamdas) {
      if (propertyLamdas == null || propertyLamdas.Length == 0) RaisePropertyChangedCore();
      else
        foreach (var pl in propertyLamdas) {
          RaisePropertyChanged(pl);
        }
    }
    protected void RaisePropertyChanged(Expression<Func<object>> propertyLamda) {
      var body = propertyLamda.Body as UnaryExpression;
      if (body == null) {
        RaisePropertyChangedCore(propertyLamda.GetLambda());
      } else {
        var operand = body.Operand as MemberExpression;
        var member = operand.Member;
        RaisePropertyChangedCore(member.Name);
      }
    }
    protected void RaisePropertyChanged(string propertyName) {
      RaisePropertyChangedCore(propertyName);
    }
    protected void RaisePropertyChangedCore(params string[] propertyNames) {
      if (PropertyChangedEvent == null) return;
      if (propertyNames.Length == 0)
        propertyNames = new[] { new StackFrame(1).GetMethod().Name.Substring(4) };
      foreach (var pn in propertyNames)
        //Application.Current.MainWindow.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => {
          PropertyChangedEvent(this, new PropertyChangedEventArgs(pn));
        //}));
    }
    #endregion
  }
  public class WindowModel : Window,INotifyPropertyChanged {

    public virtual void Checked(object sender, RoutedEventArgs e) {
      var chb = (sender as CheckBox);
      var name = chb.Name;
      this.SetProperty("_" + name, chb.IsChecked);
    }


    #region INotifyPropertyChanged Members
    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string propertyName) {
      RaisePropertyChangedCore(propertyName);
    }
    protected void RaisePropertyChanged(params Expression<Func<object>>[] propertyLamdas) {
      if (propertyLamdas == null || propertyLamdas.Length == 0) RaisePropertyChangedCore();
      else
        foreach (var pl in propertyLamdas) {
          RaisePropertyChanged(pl);
        }
    }
    protected void RaisePropertyChanged(Expression<Func<object>> propertyLamda) {
      var body = propertyLamda.Body as UnaryExpression;
      if (body == null) {
        RaisePropertyChangedCore(propertyLamda.GetLambda());
      } else {
        var operand = body.Operand as MemberExpression;
        var member = operand.Member;
        RaisePropertyChangedCore(member.Name);
      }
    }
    protected void RaisePropertyChangedCore(params string[] propertyNames) {
      if (PropertyChanged == null) return;
      if (propertyNames.Length == 0)
        propertyNames = new[] { new System.Diagnostics.StackFrame(1).GetMethod().Name.Substring(4) };
      foreach (var pn in propertyNames)
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => {
          PropertyChanged(this, new PropertyChangedEventArgs(pn));
        }));
    }
    #endregion
  }
  public class UserControlModel : UserControl, INotifyPropertyChanged {
    public virtual void Checked(object sender, RoutedEventArgs e) {
      var chb = (sender as CheckBox);
      var name = chb.Name;
      this.SetProperty("_" + name, chb.IsChecked);
    }

    #region INotifyPropertyChanged Members
    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string propertyName) {
      RaisePropertyChangedCore(propertyName);
    }
    protected void RaisePropertyChanged(params Expression<Func<object>>[] propertyLamdas) {
      if (propertyLamdas == null || propertyLamdas.Length == 0) RaisePropertyChangedCore();
      else
        foreach (var pl in propertyLamdas) {
          RaisePropertyChanged(pl);
        }
    }
    protected void RaisePropertyChanged(Expression<Func<object>> propertyLamda) {
      var body = propertyLamda.Body as UnaryExpression;
      if (body == null) {
        RaisePropertyChangedCore(propertyLamda.GetLambda());
      } else {
        var operand = body.Operand as MemberExpression;
        var member = operand.Member;
        RaisePropertyChangedCore(member.Name);
      }
    }
    protected void RaisePropertyChangedCore(params string[] propertyNames) {
      if (PropertyChanged == null) return;
      if (propertyNames.Length == 0)
        propertyNames = new[] { new System.Diagnostics.StackFrame(1).GetMethod().Name.Substring(4) };
      foreach (var pn in propertyNames)
//        Dispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => {
          PropertyChanged(this, new PropertyChangedEventArgs(pn));
  //      }));
    }
    #endregion
  }

  public class ObservableValue<TValue>:ModelBase{
    TValue _Previous;
    public TValue Previous {
      get { return _Previous; }
      set { _Previous = value; }
    }
    private TValue _Value;
    public TValue Value {
      get { return _Value; }
      set {
        if (_Value != null && _Value.Equals(value)) return;
        Previous = _Value;
        _Value = value;
        RaisePropertyChanged("Value");
        RaiseValueChanged();
      }
    }
    #region ValueChanged Event
    event EventHandler<EventArgs> ValueChangedEvent;
    public event EventHandler<EventArgs> ValueChanged {
      add {
        if (ValueChangedEvent == null || !ValueChangedEvent.GetInvocationList().Contains(value))
          ValueChangedEvent += value;
      }
      remove {
        ValueChangedEvent -= value;
      }
    }
    protected void RaiseValueChanged() {
      if (ValueChangedEvent != null) ValueChangedEvent(this, new EventArgs());
    }
    #endregion

    public ObservableValue(TValue value) {
      this.Value = value;
    }
    ~ObservableValue() {
      if (ValueChangedEvent!=null)
        ValueChangedEvent.GetInvocationList().OfType<EventHandler<EventArgs>>().ToList().ForEach(eh => ValueChangedEvent -= eh);
    }

  }

}
