using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog {
  public static class GroupAdjacentExtention {
    public static IEnumerable<TSource>
      DistinctLastUntilChanged<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TKey, TKey, bool> keyComparer = null) {
        keyComparer = keyComparer ?? EqualityComparer<TKey>.Default.Equals;
      TKey lastKey = default(TKey);
      TSource last = default(TSource);
      bool haveLast = false;
      foreach (TSource s in source) {
        TKey k = keySelector(s);
        if (haveLast) {
          if (!keyComparer(k, lastKey)) {
            yield return last;
            lastKey = k;
          }
          last = s;
        } else {
          last = s;
          lastKey = k;
          haveLast = true;
        }
      }
      if (haveLast)
        yield return last;
    }
    public static IEnumerable<IGrouping<TKey, TSource>>
      GroupByAdjacent<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TKey, TKey, bool> keyComparer = null) {
        keyComparer = keyComparer ?? EqualityComparer<TKey>.Default.Equals;
      TKey last = default(TKey);
      bool haveLast = false;
      List<TSource> list = new List<TSource>();
      foreach (TSource s in source) {
        TKey k = keySelector(s);
        if (haveLast) {
          if (!keyComparer(k, last)) {
            yield return new GroupOfAdjacent<TSource, TKey>(list, last);
            list = new List<TSource>();
            list.Add(s);
            last = k;
          } else {
            list.Add(s);
          }
        } else {
          list.Add(s);
          last = k;
          haveLast = true;
        }
      }
      if (haveLast)
        yield return new GroupOfAdjacent<TSource, TKey>(list, last);
    }
  }
  class GroupOfAdjacent<TSource, TKey> : IEnumerable<TSource>, IGrouping<TKey, TSource> {
    public TKey Key { get; set; }
    private List<TSource> GroupList { get; set; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
      return ((System.Collections.Generic.IEnumerable<TSource>)this).GetEnumerator();
    }
    System.Collections.Generic.IEnumerator<TSource> System.Collections.Generic.IEnumerable<TSource>.GetEnumerator() {
      foreach (var s in GroupList)
        yield return s;
    }
    public GroupOfAdjacent(List<TSource> source, TKey key) {
      GroupList = source;
      Key = key;
    }
  }
}
