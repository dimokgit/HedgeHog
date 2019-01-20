using IBApi;
using System;
using System.Linq;

namespace IBApp {
  public static class OrderConditionParam {
    public static OrderCondition PriceCondition(this Contract contract, double conditionPrice, bool isMore, bool isConjunction) {
      //! [price_condition]
      //Conditions have to be created via the OrderCondition.Create 
      PriceCondition priceCondition = (PriceCondition)OrderCondition.Create(OrderConditionType.Price);
      //When this contract...
      priceCondition.ConId = contract.ConId;
      priceCondition.Exchange = contract.Exchange;
      //has a price above/below
      priceCondition.IsMore = isMore;
      //this quantity
      priceCondition.Price = conditionPrice;
      //AND | OR next condition (will be ignored if no more conditions are added)
      priceCondition.IsConjunctionConnection = isConjunction;
      //! [price_condition]
      return priceCondition;
    }
    public static TimeCondition TimeCondition(this DateTime datetime, bool isMore = true, bool isConjunction = false) {
      //! [time_condition]
      TimeCondition timeCondition = (TimeCondition)OrderCondition.Create(OrderConditionType.Time);
      //Before or after...
      timeCondition.IsMore = isMore;
      //this time..
      timeCondition.Time = datetime.ToTWSString();
      //AND | OR next condition (will be ignored if no more conditions are added)     
      timeCondition.IsConjunctionConnection = isConjunction;
      //! [time_condition]
      return timeCondition;
    }

  }
  //private void OnUpdateError(int reqId, int code, string error, Exception exc) {
  //  UseOrderContracts(orderContracts => {
  //    if(!orderContracts.TryGetValue(reqId, out var oc)) return;
  //    if(new[] { /*103, 110,*/ 200, 201, 202, 203, 382, 383 }.Contains(code)) {
  //      //OrderStatuses.TryRemove(oc.contract?.Symbol + "", out var os);
  //      Trace($"{nameof(OnUpdateError)}: {new { reqId, code, error }}");
  //      RaiseOrderRemoved(oc);
  //      orderContracts.TryRemove(reqId, out var oc2);
  //    }
  //    switch(code) {
  //      case 404:
  //        var contract = oc.contract + "";
  //        var order = oc.order + "";
  //        _verbous(new { contract, code, error, order });
  //        _defaultMessageHandler("Request Global Cancel");
  //        CancelAllOrders("Request Global Cancel");
  //        break;
  //    }
  //  });
  //}
}
