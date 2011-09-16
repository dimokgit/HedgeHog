using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog {
  public class KeyEqualityComparer<T> : IEqualityComparer<T> {
    private readonly Func<T, T, bool> comparer;
    private readonly Func<T, object> keyExtractor;     // Allows us to simply specify the key to compare with: y => y.CustomerID    
    public KeyEqualityComparer(Func<T, object> keyExtractor) : this(keyExtractor, null) { }    // Allows us to tell if two objects are equal: (x, y) => y.CustomerID == x.CustomerID    
    public KeyEqualityComparer(Func<T, T, bool> comparer) : this(null, comparer) { }
    public KeyEqualityComparer(Func<T, object> keyExtractor, Func<T, T, bool> comparer) {
      this.keyExtractor = keyExtractor;
      this.comparer = comparer;
    }
    public bool Equals(T x, T y) {
      if (comparer != null) return comparer(x, y);
      else {
        var valX = keyExtractor(x); if (valX is IEnumerable<object>) // The special case where we pass a list of keys                
          return ((IEnumerable<object>)valX).SequenceEqual((IEnumerable<object>)keyExtractor(y));
        return valX.Equals(keyExtractor(y));
      }
    }
    public int GetHashCode(T obj) {
      if (keyExtractor == null)
        return obj.ToString().ToLower().GetHashCode();
      else {
        var val = keyExtractor(obj);
        if (val is IEnumerable<object>) // The special case where we pass a list of keys
          return (int)((IEnumerable<object>)val).Aggregate((x, y) => x.GetHashCode() ^ y.GetHashCode());
        return val.GetHashCode();
      }
    }
  }
}
