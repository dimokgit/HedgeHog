using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IBApi {
  partial class ComboLeg {
    public bool IsBuy => Action.ToUpper() == "BUY";
    public int Quantity => IsBuy ? Ratio : -Ratio;

    public override bool Equals(object obj) => obj is ComboLeg leg && ConId == leg.ConId && Ratio == leg.Ratio && Action == leg.Action;

    public override int GetHashCode() {
      var hashCode = 228620681;
      hashCode = hashCode * -1521134295 + ConId.GetHashCode();
      hashCode = hashCode * -1521134295 + Ratio.GetHashCode();
      hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Action);
      return hashCode;
    }
  }
}
