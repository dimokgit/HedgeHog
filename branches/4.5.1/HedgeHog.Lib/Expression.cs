using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class ExpressionExtenssions {

    static void A() {
      var expr = Express((string x) => new { x, y = x.Length });
      var z = expr.Compile()("dimok").x;
    }
    public static Expression<Func<M, T>> Express<M,T>(Expression<Func<M, T>> expression) {
      return expression;
    }
  }
}
