using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IBApi {
  partial class ComboLeg {
    public bool IsBuy => Action.ToUpper() == "BUY";
  }
}
