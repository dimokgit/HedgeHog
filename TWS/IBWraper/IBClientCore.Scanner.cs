using HedgeHog.Shared;
using IBApi;
using IBSampleApp.messages;
using System.Reactive.Linq;
using System;
using ScannerHandler = System.Action<IBSampleApp.messages.ScannerMessage>;
using ScannerParametersHandler = System.Action<string>;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using IBSamples;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace IBApp {
  public partial class IBClientCore :IBClient, ICoreFX {
    // https://github.com/benofben/interactive-brokers-api/blob/master/JavaClient/src/com/ib/controller/ScanCode.java
    // https://interactivebrokers.github.io/tws-api/market_scanners.html
    public static string[] SCAN_CEDES = new string[] {
  "HALTED",
  "HIGH_OPEN_GAP",
  "HIGH_OPT_IMP_VOLAT",
  "HIGH_OPT_IMP_VOLAT_OVER_HIST",
  "HIGH_OPT_OPEN_INTEREST_PUT_CALL_RATIO",
  "HIGH_OPT_VOLUME_PUT_CALL_RATIO",
  "HIGH_SYNTH_BID_REV_NAT_YIELD",
  "HIGH_VS_13W_HL",
  "HIGH_VS_26W_HL",
  "HIGH_VS_52W_HL",
  "HOT_BY_OPT_VOLUME",
  "HOT_BY_PRICE",
  "HOT_BY_PRICE_RANGE",
  "HOT_BY_VOLUME",
  "LOW_OPEN_GAP",
  "LOW_OPT_IMP_VOLAT",
  "LOW_OPT_IMP_VOLAT_OVER_HIST",
  "LOW_OPT_OPEN_INTEREST_PUT_CALL_RATIO",
  "LOW_OPT_VOL_PUT_CALL_RATIO",
  "LOW_OPT_VOLUME_PUT_CALL_RATIO",
  "LOW_SYNTH_BID_REV_NAT_YIELD",
  "LOW_VS_13W_HL",
  "LOW_VS_26W_HL",
  "LOW_VS_52W_HL",
  "MOST_ACTIVE",
  "NOT_OPEN",
  "OPT_OPEN_INTEREST_MOST_ACTIVE",
  "OPT_VOLUME_MOST_ACTIVE",
  "TOP_OPEN_PERC_GAIN",
  "TOP_OPEN_PERC_LOSE",
  "TOP_OPT_IMP_VOLAT_GAIN",
  "TOP_OPT_IMP_VOLAT_LOSE",
  "TOP_PERC_GAIN",
  "TOP_PERC_LOSE",
  "TOP_PRICE_RANGE",
  "TOP_TRADE_COUNT",
  "TOP_TRADE_RATE",
  "TOP_VOLUME_RATE",
    };

    #region Wire Observables
    IObservable<ScannerMessage> ScannerSubscriptionFactory()
 => Observable.FromEvent<ScannerHandler, ScannerMessage>(
   onNext => (ScannerMessage m) => { onNext(m); },
   h => ScannerData += h,//.SideEffect(_ => TraceError("ContractDetails += h")),
   h => ScannerData -= h//.SideEffect(_ => TraceError("ContractDetails -= h"))
   );
    IObservable<ScannerMessage> _ScannerSubscriptionObservable;
    IObservable<ScannerMessage> ScannerSubscriptionObservable =>
      (_ScannerSubscriptionObservable ?? (_ScannerSubscriptionObservable = ScannerSubscriptionFactory()));

    IObservable<int> ScannerSubscriptionEndFactory() => Observable.FromEvent<Action<int>, int>(
        onNext => (int a) => {
          try {
            //Trace($"{nameof(ScannerSubscriptionEndFactory)}:{a}");
            onNext(a);
          } catch(Exception exc) {
            Debugger.Break();
            TraceError(exc);
          }
        },
        h => ScannerDataEnd += h,
        h => ScannerDataEnd -= h
        );
    IObservable<int> _ScannerSubscriptionEndObservable;
    IObservable<int> ScannerSubscriptionEndObservable =>
      (_ScannerSubscriptionEndObservable ?? (_ScannerSubscriptionEndObservable = ScannerSubscriptionEndFactory()));

    IObservable<string> ScannerParametersFactory()
 => Observable.FromEvent<ScannerParametersHandler, string>(
   onNext => (string m) => { onNext(m); },
   h => ScannerParameters += h,//.SideEffect(_ => TraceError("ContractDetails += h")),
   h => ScannerParameters -= h//.SideEffect(_ => TraceError("ContractDetails -= h"))
   );
    IObservable<string> _ScannerParametersObservable;
    IObservable<string> ScannerParametersObservable =>
      (_ScannerParametersObservable ?? (_ScannerParametersObservable = ScannerParametersFactory()));

    #endregion
    public IObservable<string> RegScannerParameters() {
      try {
        return ScannerParametersObservable;
      } finally {
        OnReqMktData(() => ClientSocket.reqScannerParameters());
      }
    }
    public IObservable<ContractDetails[]> ReqScannerSubscription(string scanCode) {
      var reqId = NextReqId();
      //Hot US stocks by volume
      ScannerSubscription scanSub = new ScannerSubscription();
      scanSub.NumberOfRows = 100;
      scanSub.Instrument = "STK";
      scanSub.LocationCode = "STK.US";
      scanSub.ScanCode = scanCode;// "TOP_PERC_GAIN";//"HOT_BY_PRICE_RANGE";// "HOT_BY_PRICE";// "HIGH_OPT_IMP_VOLAT_OVER_HIST";// "HIGH_OPT_IMP_VOLAT";// "HIGH_OPEN_GAP";// "TOP_OPEN_PERC_GAIN";// "HOT_BY_VOLUME";
      scanSub.MarketCapAbove = 200000;
      scanSub.AboveVolume = 5000000;
      var context = scanSub.ScanCode;
      var options = new List<TagValue> {
      new TagValue("changePercAbove", "20"),
      new TagValue("priceAbove", "5"),
    new TagValue("priceBelow", "5")
      };

      var cd = WireToError(
        reqId,
        ScannerSubscriptionObservable,
        ScannerSubscriptionEndObservable,
        t => t.RequestId,
        error => {
          TraceError($"{nameof(ReqScannerSubscription)}:{new { context, error.errorMsg }}");
          return true;
        }
        )
        .ToArray()
        .Do(a => {
          if(a.IsEmpty()) {
            TraceError($"Scan context {context} not found");
          }
          a.ForEach(t => {
            if(t.ContractDetails.Contract.Exchange == "QBALGO") t.ContractDetails.Contract.Exchange = "GLOBEX";
            t.ContractDetails.AddToCache();
          });
        })
        .SelectMany(a => a.Select(t => t.ContractDetails))
        .ToArray()
        .Do(_ => ClientSocket.cancelScannerSubscription(reqId))
        ;
      OnReqMktData(() => ClientSocket.reqScannerSubscription(reqId, scanSub, options));
      return cd;
    }

  }
}