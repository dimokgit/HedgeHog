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

  public class ObservableObject : INotifyPropertyChanged {
    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void SetAndNotify<T>(ref T field, T value, Expression<Func<T>> property) {
      if (!object.ReferenceEquals(field, value)) {
        field = value;
        this.OnPropertyChanged(property);
      }
    }

    protected virtual void OnPropertyChanged<T>(Expression<Func<T>> changedProperty) {
      if (PropertyChanged != null) {
        string name = ((MemberExpression)changedProperty.Body).Member.Name;
        PropertyChanged(this, new PropertyChangedEventArgs(name));
      }
    }
  }
  public class Customer : ObservableObject {
    private string name;

    public string Name {
      get { return this.name; }
      set { this.SetAndNotify(ref this.name, value, () => this.Name); }
    }
  }
  public class ValueTrigger {
    bool _on = false;
    public bool On { get { return _on; } }
    public ValueTrigger(bool on) {
      this._on = on;
    }
    public bool Set(bool on) {
      if (!_on)
        _on = on;
      return _on;
    }
    public void Off() { _on = false; }
  }
  public class ObservableValue<TValue>:ModelBase{
    bool _changeTo = false;
    TValue _changeToValue;
    TValue _Previous;
    public TValue Previous {
      get { return _Previous; }
      set { _Previous = value; }
    }
    private TValue _Value;
    public TValue Value {
      get { return _Value; }
      set {
        HasChanged = HasChangedTo = false;
        if (_Value != null && _Value.Equals(value)) return;
        Previous = _Value;
        _Value = value;
        HasChanged = true;
        RaisePropertyChanged("Value");
        RaiseValueChanged();
        if (_changeTo && _Value.Equals(_changeToValue)) {
          HasChangedTo = true;
          RaiseValueChangedTo();
        }
      }
    }
    public ObservableValue<TValue> SetValue(TValue value) {
      this.Value = value;
      return this;
    }
    public bool ChangedTo(TValue value) {
      return HasChanged && Value.Equals(value);
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
    #region ValueChangedTo Event
    event EventHandler<EventArgs> ValueChangedToEvent;
    public event EventHandler<EventArgs> ValueChangedTo {
      add {
        if (ValueChangedToEvent == null || !ValueChangedToEvent.GetInvocationList().Contains(value))
          ValueChangedToEvent += value;
      }
      remove {
        ValueChangedToEvent -= value;
      }
    }
    protected void RaiseValueChangedTo() {
      if (ValueChangedToEvent != null) ValueChangedToEvent(this, new EventArgs());
    }
    #endregion

    public ObservableValue(TValue value,TValue valueTo) :this(value){
      _changeTo = true;
      _changeToValue = valueTo;
    }
    public ObservableValue(TValue value) {
      this.Value = value;
    }
    ~ObservableValue() {
      if (ValueChangedEvent!=null)
        ValueChangedEvent.GetInvocationList().OfType<EventHandler<EventArgs>>().ToList().ForEach(eh => ValueChangedEvent -= eh);
    }


    public bool HasChanged { get; set; }
    public bool HasChangedTo { get; set; }
  }

}
