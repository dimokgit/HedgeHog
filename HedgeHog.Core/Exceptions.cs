using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class Exceptions {
    public static IEnumerable<Exception> Inners(this Exception exc, params Type[] exclude) {
      if(exc == null) yield break;
      Func<Exception, bool> mustExclude = e => exclude.Contains(e.GetType());
      if(exc is AggregateException)
        foreach(var e in ((AggregateException)exc).Flatten().InnerExceptions.SelectMany(e => e.Inners()))
          yield return e;
      else
        if(exc != null) {
        if(!mustExclude(exc))
          yield return exc;
        foreach(var exc2 in exc.InnerException.Inners())
          if(!mustExclude(exc2))
            yield return exc2;
      }
    }
  }
}
