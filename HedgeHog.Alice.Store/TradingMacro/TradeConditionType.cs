using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Alice.Store {
  public class TradeDirectionTriggerAttribute : Attribute { }
  public class TradeConditionAttribute : Attribute {
    public enum Types { And, Or };
    public TradeConditionAttribute(Types type = Types.And) {
      this.Type = type;
    }

    public Types Type { get; set; }
  }
}
