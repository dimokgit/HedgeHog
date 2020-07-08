using HedgeHog;
using System.Collections.Generic;
using System;
using System.Linq;

namespace IBApi {
  public static class OrderMixins {
    public static string ToText(this IList<OrderCondition> conditions, string prefix = "", bool showPrice = true)
      => conditions.ToTexts(showPrice: showPrice).Flatter(";").With(t => t.IsNullOrEmpty() ? "" : prefix + t);
    public static IEnumerable<string> ToTexts(this IList<OrderCondition> conditions, string prefix = "", bool showPrice = true)
      => conditions.SelectMany(c => 
      c.ParsePriceCondition().Select(pc => pc.contract + " " + pc.@operator + (showPrice ? " " + pc.price : ""))
      .Concat(c.ParseTimeCondition())
      );
    public static IEnumerable<string> ParseTimeCondition(this OrderCondition oc) {
      var tc = oc as TimeCondition;
      if(tc != null) {
        var d = tc.Time.FromTWSString();
        var ds = d.Date <= DateTime.Now.Date ? d.ToString("HH:mm") : d.ToString("dd HH:mm");
        yield return $"time {tc.IsMore.IsMore()} {ds}";
      }
    }
    public static IEnumerable<(string contract, string @operator, double price)> ParsePriceCondition(this OrderCondition oc) {
      var pc = oc as PriceCondition;
      if(pc == null) yield break;
      var o = pc.IsMore.IsMore();
      foreach(var c in Contract.FromCache(co => co.ConId == pc.ConId).Select(co => co.LocalSymbol).DefaultIfEmpty(pc.ContractResolver(pc.ConId, pc.Exchange)))
        yield return (c, o, pc.Price);
    }
    static string IsMore(this bool isMore) => isMore ? ">=" : "<=";
  }
}
