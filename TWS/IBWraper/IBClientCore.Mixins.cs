using IBApi;
using System;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;

namespace IBApp {
  public static class IBClientCoreMixins {
    public static IObservable<MarketPrice> ReqPriceSafe(this Contract contract, double timeoutInSeconds, bool useErrorHandler, double defaultPrice) =>
  IBClientCore.IBClientCoreMaster.MarketDataManager.ReqPriceSafe(contract, timeoutInSeconds, useErrorHandler, defaultPrice).DefaultIfEmpty((defaultPrice, defaultPrice, DateTime.MinValue, 0,double.NaN));

    public static IObservable<MarketPrice>
      ReqPriceSafe(this Contract contract, double timeoutInSeconds = 2, [CallerMemberName] string Caller = "")
    => IBClientCore.IBClientCoreMaster.MarketDataManager.ReqPriceSafe(contract, timeoutInSeconds, Caller);

    public static IObservable<MarketPrice>
      ReqPriceSafe(this ContractDetails cd, double timeoutInSeconds = 2, [CallerMemberName] string Caller = "")
    => cd.Contract.ReqPriceSafe(timeoutInSeconds, Caller);

    public static IObservable<ContractDetails> ReqContractDetailsCached(this string instriment) =>
      IBClientCore.IBClientCoreMaster.ReqContractDetailsCached(instriment);
    public static IObservable<ContractDetails> ReqContractDetailsCached(this Contract c) =>
      IBClientCore.IBClientCoreMaster.ReqContractDetailsCached(c);
    public static double Price(this MarketPrice p, bool isBuy) => isBuy ? p.ask : p.bid;
  }
}