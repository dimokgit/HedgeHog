using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;

namespace HedgeHog {
  public static class ExpressionExtentions {
    public static string GetLambda(Expression<Func<object>> func) { return func.Name(); }
    public static string[] GetLambdas(params Expression<Func<object>>[] funcs) { return funcs.Names(); }
    public static string[] Names(this Expression<Func<object>>[] funcs) {
      var names = new List<string>();
      foreach (var e in funcs)
        names.Add(e.Name());
      return names.ToArray();
    }

    public static string Name(this Expression<Func<object>> propertyLamda) {
      var body = propertyLamda.Body as UnaryExpression;
      if (body == null) {
        return ((propertyLamda as LambdaExpression).Body as MemberExpression).Member.Name;
      } else {
        var operand = body.Operand as MemberExpression;
        var member = operand.Member;
        return member.Name;
      }
    }

    public static string Name(this LambdaExpression propertyExpression) {
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
      return body.Member.Name;
    }

  }
}
