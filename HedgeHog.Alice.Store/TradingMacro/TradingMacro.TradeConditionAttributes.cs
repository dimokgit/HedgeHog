using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Store {
  class TradeConditionStartDateTriggerAttribute : Attribute { }
  class TradeConditionAsleepAttribute : Attribute { }
  class TradeConditionTurnOffAttribute : Attribute { }
  class TradeConditionSetCorridorAttribute : Attribute { }
  class TradeConditionCanSetCorridorAttribute : Attribute { }
  class TradeConditionTradeStripAttribute : Attribute { }
  class TradeConditionTradeDirectionAttribute : Attribute { }
  class TradeConditionByRatioAttribute : Attribute { }
  class TradeConditionShouldCloseAttribute :Attribute { }
}
