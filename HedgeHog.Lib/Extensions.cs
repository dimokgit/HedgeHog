using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Data.Objects;
using System.Linq.Expressions;
using System.Reflection;

namespace HedgeHog {

  namespace ConsoleApplication1 {
    public static class ObjectQueryExtensions {
      public static ObjectQuery<TSource> Include<TSource, TResult>(this ObjectQuery<TSource> query, Expression<Func<TSource, TResult>> path) {
        if (query == null) throw new ArgumentException("query");
        var properties = new List<string>();
        Action<string> add = (str) => properties.Insert(0, str); 
        var expression = path.Body; 
        do {
          switch (expression.NodeType) {
            case ExpressionType.MemberAccess: 
              var member = (MemberExpression)expression; 
              if (member.Member.MemberType != MemberTypes.Property) 
                throw new ArgumentException("The selected member must be a property.", "selector"); 
                add(member.Member.Name); 
                expression = member.Expression; 
                break;
            case ExpressionType.Call: 
              var method = (MethodCallExpression)expression;
              if (method.Method.Name != SingleMethodName || method.Method.DeclaringType != EnumerableType)
                throw new ArgumentException(string.Format("Method '{0}' is not supported, only method '{1}' is supported to singularize navigation properties."
                  , string.Join(Type.Delimiter.ToString(), method.Method.DeclaringType.FullName, method.Method.Name)
                  , string.Join(Type.Delimiter.ToString(), EnumerableType.FullName, SingleMethodName)), "selector");
              expression = (MemberExpression)method.Arguments.Single(); break;
            default:
              throw new ArgumentException("The property selector expression has an incorrect format.", "selector", new FormatException());
          }
        } while (expression.NodeType != ExpressionType.Parameter); 
        return query.Include(string.Join(Type.Delimiter.ToString(), properties));
      } 
      private static readonly Type EnumerableType = typeof(Enumerable); 
      private const string SingleMethodName = "Single";
    }
  }

  public static class DispatcherEx {
    public static void Invoke(this Dispatcher d, Action action) {
      if (d.CheckAccess()) {
        action();
      } else {
        d.Invoke(action);
      }
    }
  }
  public static class DependencyObjectExtensions {
    public static TParent GetParent<TParent>(this DependencyObject dp) where TParent : DependencyObject {
      if (dp == null) return null;
      var fwParent = dp is FrameworkElement ? ((FrameworkElement)dp).Parent : null;
      if (fwParent != null && fwParent is TParent) return fwParent as TParent;
      var vtParent = VisualTreeHelper.GetParent(dp);
      if (vtParent != null && vtParent is TParent) return vtParent as TParent;
      return GetParent<TParent>(fwParent) ?? GetParent<TParent>(vtParent);
    }
  }
}
