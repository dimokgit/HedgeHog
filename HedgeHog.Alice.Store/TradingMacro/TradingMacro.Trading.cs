using HedgeHog.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    object _tradeLock = new object();
    public void OpenTrade(bool isBuy, int lot, Price price, string reason) {
      var me = Common.Caller();
      lock(_tradeLock) {
        var key = lot - Trades.Lots(t => t.IsBuy != isBuy) > 0 ? OT : CT;
        CheckPendingAction(key, (pa) => {
          if(lot > 0) {
            pa();
            LogTradingAction($"{this}: {(isBuy ? "Buying" : "Selling")} {lot} by {me} {new { reason }}");
            TradesManager.OpenTrade(Pair, isBuy, lot, 0, 0, "", price);
          }
        });
      }
    }

    public void CloseTrades(Price price, string reason) { CloseTrades(Trades.Lots(), price, reason); }
    //[MethodImpl(MethodImplOptions.Synchronized)]
    private void CloseTrades(int lot, Price price, string reason) {
      var me = Common.Caller();
      lock(_tradeLock) {
        if(!IsTrader || !Trades.Any() || HasPendingKey(CT))
          return;
        if(lot > 0)
          CheckPendingAction(CT, pa => {
            LogTradingAction($"{this}: Closing {lot} out of {Trades.Lots()} by {me} {new { reason }}]");
            pa();
            if(!TradesManager.ClosePair(Pair, Trades[0].IsBuy, lot, price))
              ReleasePendingAction(CT);
          });
      }
    }
  }
}
