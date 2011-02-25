using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Linq.Expressions;

namespace HedgeHog {
  public static class PropertyChangedExtensions {
    public static string GetLambda(this LambdaExpression propertyExpression) {
      var m = propertyExpression.Body.GetPropertyLocal("Member");
      return m.GetPropertyLocal("Name") + "";
    }
    public static void Raise(this PropertyChangedEventHandler handler, LambdaExpression propertyExpression) {
      if (handler != null) {
        handler(null, new PropertyChangedEventArgs(propertyExpression.GetLambda()));
        /*
        // Retreive lambda body
        var body = propertyExpression.Body as MemberExpression;
        if (body == null)
          throw new ArgumentException("'propertyExpression' should be a member expression");

        // Extract the right part (after "=>")
        var vmExpression = body.Expression as ConstantExpression;
        if (vmExpression == null)
          throw new ArgumentException("'propertyExpression' body should be a constant expression");

        // Create a reference to the calling object to pass it as the sender
        LambdaExpression vmlambda = System.Linq.Expressions.Expression.Lambda(vmExpression);
        Delegate vmFunc = vmlambda.Compile();
        object vm = vmFunc.DynamicInvoke();

        // Extract the name of the property to raise a change on
        string propertyName = body.Member.Name;
        var e = new PropertyChangedEventArgs(propertyName);
        handler(vm, e);
        */
      }
    }
    static object GetPropertyLocal(this object o, string name) { return o.GetType().GetProperty(name).GetValue(o, null); }
  }
}
