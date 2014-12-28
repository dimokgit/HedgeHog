using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  /// <summary>
  /// http://www.pcreview.co.uk/forums/root-finding-implementation-c-t3765074.html
  /// </summary>
  public class Solver {
    // Newtons formel for iterativ lignings løsning
    private const double DELTA = 0.0000001;
    public delegate double FX(double x);
    public static double FindZero(FX f) {
      double x, xnext = 0;
      do {
        x = xnext;
        xnext = x - f(x) / ((f(x + DELTA) - f(x)) / DELTA);
      } while (Math.Abs(xnext - x) > DELTA);
      return xnext;
    }
    // løs simple trediegrads polynomium y=x^3+x-30 med en enkelt
    //løsning x=3
    public static void main(string[] args) {
      Console.WriteLine(Solver.FindZero((double x) => x * x * x + x -
      30));
    }
  }
}
