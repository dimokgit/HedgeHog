using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class IEnumerableCore {
    public static IEnumerable<T> IfEmpty<T>(this IEnumerable<T> enumerable,
          Action emptyAction) {

      var isEmpty = true;
      foreach (var e in enumerable) {
        isEmpty = false;
        yield return e;
      }
      if (isEmpty)
        emptyAction();
    }
    public static IEnumerable<T> IfEmpty<T>(this IEnumerable<T> enumerable,
          Func<IEnumerable<T>> emptySelector) {

      var isEmpty = true;
      foreach (var e in enumerable) {
        isEmpty = false;
        yield return e;
      }
      if (isEmpty)
        foreach (var e in emptySelector())
          yield return e;
    }
    public static T IfEmpty<T>(this T enumerable,
          Action<T> thenSelector,
          Action<T> elseSelector) where T : IEnumerable {

      if (!enumerable.GetEnumerator().MoveNext()) thenSelector(enumerable); else elseSelector(enumerable);
      return enumerable;
    }
    public static TResult IfEmpty<T, TResult>(this IEnumerable<T> enumerable,
          Func<TResult> thenSelector,
          Func<TResult> elseSelector) {

      return (!enumerable.Any()) ? thenSelector() : elseSelector();
    }
  }
}
