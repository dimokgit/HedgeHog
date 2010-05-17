using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq.Expressions;

namespace HedgeHog.Alice.Client {
  public class TradingAccountModel : Shared.Account, INotifyPropertyChanged {
    public void OnPropertyChanged(params Expression<Func<object>>[] propertyLamdas) {
      foreach (var pl in propertyLamdas) 
        RaisePropertyChanged(pl);
    }
    protected void RaisePropertyChanged(Expression<Func<object>> propertyLamda) {
      var bodyUE = propertyLamda.Body as UnaryExpression;
      if (bodyUE == null) {
        var s = (propertyLamda.Body as Expression).ToString().Split('.');
        RaisePropertyChanged(s.Last().Split(')')[0]);
      } else {
        var operand = bodyUE.Operand as MemberExpression;
        var member = operand.Member;
        RaisePropertyChanged(member.Name);
      }
    }

    protected virtual void RaisePropertyChanged(params LambdaExpression[] propertyExpressions) {
      if (PropertyChanged != null)
        foreach (var propertyExpression in propertyExpressions)
          PropertyChanged.Raise(propertyExpression);
      }
    protected virtual void RaisePropertyChanged(string propertyName) {
      VerifyPropertyName(propertyName);

      var handler = PropertyChanged;

      if (handler != null) {
        handler(this, new PropertyChangedEventArgs(propertyName));
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

    #region INotifyPropertyChanged Members

    public event PropertyChangedEventHandler PropertyChanged;

    #endregion
  }
}
