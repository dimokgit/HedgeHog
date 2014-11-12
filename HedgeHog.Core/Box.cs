using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog{
  public class Box<T> {
    T _value;
    public T Value {
      get { return _value; }
      set {
        if (_value.Equals(value)) return;
        _value = value;
        if (PropertyChanged != null) PropertyChanged(this, Value);
      }
    }
    public Box(T value, EventHandler<T> eventHandler)
      : this(value) {
      PropertyChanged += eventHandler;
    }
    public Box(T value) {
      Value = value;
    }
    public Box() {

    }
    ~Box() {
      if (PropertyChanged != null)
        PropertyChanged.GetInvocationList().ToList().ForEach(d => PropertyChanged -= d as EventHandler<T>);
    }
    public override string ToString() {
      return Value.ToString();
    }
    public static implicit operator T(Box<T> m) {
      return m.Value;
    }
    //public static implicit operator Box<T>(T m) {
    //  return new Box<T>(m);
    //}

    public event EventHandler<T> PropertyChanged;
  }
  public static class BoxMixin {
    public static Box<T> ToBox<T>(this T value) {
      return new Box<T>(value);
    }

  }
}
