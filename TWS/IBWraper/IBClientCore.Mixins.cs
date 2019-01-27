using IBApi;
using System;
using System.Runtime.CompilerServices;

namespace IBApp {
  public static class IBClientCoreMixins {
    public static IObservable<(double bid, double ask, DateTime time, double delta)>
      ReqPriceSafe(this Contract contract, double timeoutInSeconds = 2, [CallerMemberName] string Caller = "")
    => IBClientCore.IBClientCoreMaster.ReqPriceSafe(contract, timeoutInSeconds, Caller);
  }
}