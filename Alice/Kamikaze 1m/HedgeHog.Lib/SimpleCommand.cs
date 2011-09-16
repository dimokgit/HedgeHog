using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;

namespace HedgeHog {
  public class SimpleDelegateCommand : ICommand {
    // Specify the keys and mouse actions that invoke the command. 
    public Key GestureKey { get; set; }
    public ModifierKeys GestureModifier { get; set; }
    public MouseAction MouseGesture { get; set; }

    Action<object> _executeDelegate;

    public SimpleDelegateCommand(Action<object> executeDelegate) {
      _executeDelegate = executeDelegate;
    }

    public void Execute(object parameter) {
      _executeDelegate(parameter);
    }

    public bool CanExecute(object parameter) { return true; }

    #region ICommand Members
    public event EventHandler CanExecuteChanged;
    void Fuck() { CanExecuteChanged(null, null); }

    #endregion
  }
}
