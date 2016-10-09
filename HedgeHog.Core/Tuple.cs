using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class TupleHedgeHogExtentions {
    public static Tuple<T1, T2> Tuple<T1, T2>(T1 item1, T2 item2) => 
      System.Tuple.Create(item1, item2);
    public static Tuple<T1, T2,T3> Tuple<T1, T2,T3>(T1 item1, T2 item2, T3 item3) => 
      System.Tuple.Create(item1, item2,item3);
    public static Tuple<T1, T2, T3, T4> Tuple<T1, T2, T3, T4>(T1 item1, T2 item2, T3 item3, T4 item4) => 
      System.Tuple.Create(item1, item2, item3, item4);

    public static R Map<T1, T2, R>(this Tuple<T1, T2> self, Func<T1, T2, R> map) =>
        map(self.Item1, self.Item2);
    public static R Map<T1, T2, T3, R>(this Tuple<T1, T2, T3> self, Func<T1, T2, T3, R> map) =>
        map(self.Item1, self.Item2, self.Item3);
    public static R Map<T1, T2, T3, T4, R>(this Tuple<T1, T2, T3, T4> self, Func<T1, T2, T3, T4, R> map) =>
        map(self.Item1, self.Item2, self.Item3, self.Item4);
  }
}
