using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Temp {
  public partial class ClosedTrade {
    public int NetPL { get { return (int)Math.Round(GrossPL - Commission, 0); } }
    public int AmountK { get { return Lots / 1000; } }
    public DateTime DateClose { get { return TimeClose.Date; } }
  }
}
