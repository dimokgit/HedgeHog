using HedgeHog;
using System.Collections.Generic;
using System.Linq;

namespace IBApi {
  public static class OrderMixins {
    public static string ToText(this IList<OrderCondition> conditions, string prefix = "")
      => conditions.ToTexts().Flatter(";").With(t => t.IsNullOrEmpty() ? "" : prefix + t);
    public static IEnumerable<string> ToTexts(this IList<OrderCondition> conditions, string prefix = "")
      => conditions.SelectMany(c => c.ParsePriceCondition().Select(pc => pc.contract + " " + pc.@operator + pc.price));
    public static IEnumerable<(string contract, string @operator, double price)> ParsePriceCondition(this OrderCondition oc) {
      var pc = oc as PriceCondition;
      if(pc == null) yield break;
      var o = pc.IsMore.IsMore();
      foreach(var c in Contract.FromCache(co => co.ConId == pc.ConId).Select(co => co.LocalSymbol).DefaultIfEmpty(pc.ContractResolver(pc.ConId, pc.Exchange)))
        yield return (c, o, pc.Price);
    }
    static string IsMore(this bool isMore) => isMore ? ">= " : "<= ";
  }
}
