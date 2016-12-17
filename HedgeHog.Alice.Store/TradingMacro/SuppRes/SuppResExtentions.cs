using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog;
namespace HedgeHog.Alice.Store {
  using static IEnumerableCore;
  using SuppRessesList = IEnumerableCore.Singleable<IList<SuppRes>>;
  public static class SuppResExtentions {
    static readonly Singleable<IList<SuppRes>> Empty = new Singleable<IList<SuppRes>>(new[] { (new SuppRes[0]) }.Take(0).ToList());
    public static SuppRessesList ToSupressesList(this IList<SuppRes> s) {
      return new[] { s }.AsSingleable();
    }

    public static SuppRes[] Active(this IEnumerable<SuppRes> supReses, bool isBuy) {
      return supReses.Active().IsBuy(isBuy);
    }
    static SuppRes[] Active(this IEnumerable<SuppRes> supReses) {
      return supReses.Where(sr => sr.IsActive).ToArray();
    }
    public static SuppRes[] IsBuy(this IEnumerable<SuppRes> supReses, bool isBuy) {
      return supReses.Where(sr => sr.IsBuy == isBuy).ToArray();
    }
    public static SuppRessesList If(this IList<SuppRes> supReses, Func<IList<SuppRes>, bool> condition) {
      return condition(supReses) ? supReses.ToSupressesList()  : Empty;
    }
    public static SuppRessesList If(this IList<SuppRes> supReses, Func<bool> condition) {
      return condition() ? supReses.ToSupressesList() : Empty;
    }
    public static SuppRessesList IfAllManual(this IList<SuppRes> supReses) {
      return supReses.If(() => supReses.All(sr => sr.InManual));
    }
    public static SuppRessesList IfAllNonManual(this IList<SuppRes> supReses) {
      return supReses.If(() => supReses.All(sr => !sr.InManual));
    }
    public static SuppRessesList IfAnyManual(this IList<SuppRes> supReses) {
      return supReses.If(() => supReses.Any(sr => sr.InManual));
    }
    public static SuppRessesList IfAnyCanTrade(this IList<SuppRes> supReses) {
      return supReses.If(() => supReses.Any(sr => sr.CanTrade));
    }
    public static IList<SuppRes> SetCanTrade(this IList<SuppRes> supReses, bool canTrade) {
      return supReses.Do(sr => sr.CanTrade = canTrade).ToArray();
    }
    public static IList<SuppRes> SetCanTradeEx(this IList<SuppRes> supReses, bool canTrade) {
      return supReses.Do(sr => sr.CanTradeEx = canTrade).ToArray();
    }
    public static IList<SuppRes> SetInManual(this IList<SuppRes> supReses, bool inManuale) {
      return supReses.Do(sr => sr.InManual = inManuale).ToArray();
    }
    public static void ForFirst(this SuppRessesList supReses, Action action) {
      supReses.ForEach(_ => action());
    }
    public static double Height(this IList<SuppRes> supReses) {
      return supReses.Count == 2
        ? supReses[0].Rate.Max(supReses[1].Rate)
        : supReses.Max(sr => sr.Rate).Abs(supReses.Min(sr => sr.Rate));
    }

  }
}
