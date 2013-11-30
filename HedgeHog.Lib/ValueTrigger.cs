using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog {
  public class ValueTrigger<T> {
    bool _on = false;
    private Action _actionOn;
    public bool On { get { return _on; } }
    public delegate void RunActionHandler();
    public event RunActionHandler RunAction; 
    public ValueTrigger(bool initialValue) : this(initialValue, null) { }
    public ValueTrigger(bool initialValue, Action on) {
      this._on = initialValue;
      this._actionOn = on;
    }
    public T Value;
    public ValueTrigger<T> Set(bool on, T value) {
      return Set(on, null, value);
    }
    public ValueTrigger<T> Set(bool on, Action onAction = null, T value = default(T)) {
      if (on && !_on) {
        _on = true;
        if (onAction != null) onAction();
        if (_actionOn != null) _actionOn();
        Value = value;
        if (RunAction != null) RunAction();
      }
      return this;
    }
    public ValueTrigger<T> Off(bool on, Action onOff = null) {
      if (!on) Off(onOff);
      return this;
    }
    public ValueTrigger<T> Off(Action onOff = null) {
      if (_on) {
        if (onOff != null) onOff();
      }
      _on = false;
      return this;
    }
  }

  public class Value2Trigger<T> {
    ValueTrigger<T> _vt1;
    ValueTrigger<T> _vt2;
    public bool On { get { return _vt1.On && _vt2.On; } }
    public Value2Trigger(bool initialValue) : this(initialValue, initialValue) { }
    public Value2Trigger(bool initialValue1, bool initialValue2) {
      this._vt1 = new ValueTrigger<T>(initialValue1);
      this._vt2 = new ValueTrigger<T>(initialValue2);
      this._vt1.RunAction += RunAction;
      this._vt2.RunAction += () => this._vt1.Off();
    }

    bool runAction = false;
    void RunAction() {
      runAction = On;
    }
    public T Value;
    public Value2Trigger<T> Set(bool on1, bool on2, T value) {
      return Set(on1, on2, null, value);
    }
    public Value2Trigger<T> Set(bool on1, bool on2, Action onAction = null, T value = default(T)) {
      _vt2.Set(on2);
      _vt1.Set(on1 && _vt2.On);
      if (runAction) {
        if (onAction != null) onAction();
        Value = value;
        runAction = false;
      }
      return this;
    }
    public Value2Trigger<T> Off1() {
      _vt1.Off();
      return this;
    }
    public Value2Trigger<T> Off2() {
      _vt2.Off();
      return this;
    }
    public Value2Trigger<T> Off() {
      return Off1().Off2();
    }
  }
}
