using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Bars {
  public class DataPoint {
    public double Value { get; set; }
    public DataPoint Next { get; set; }
    public DateTime Date { get; set; }
    public int Index { get; set; }
    public int Slope { get { return Math.Sign(Next.Value - Value); } }
  }
}
