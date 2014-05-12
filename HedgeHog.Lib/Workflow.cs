using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  delegate Func<A, R> RecursiveFunc<A, R>(RecursiveFunc<A, R> r);
  delegate Action<A> RecursiveAction<A>(RecursiveAction<A> r);
  public static class WorkflowMixin {
    public static Func<A,R> Y<A,R>(Func<Func<A,R>, Func<A,R>> f) {
      RecursiveFunc<A,R> rec = r => a => f(r(r))(a);
      return rec(rec);
    }
    public static Action<A> YAction<A>(Func<Action<A>, Action<A>> f) {
      RecursiveAction<A> rec = r => a => f(r(r))(a);
      return rec(rec);
    }
  }
}
