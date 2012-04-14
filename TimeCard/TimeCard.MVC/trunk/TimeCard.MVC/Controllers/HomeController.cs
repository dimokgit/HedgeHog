using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Diagnostics;
using System.Linq.Dynamic;
using TimeCard.MVC.Models;

namespace TimeCard.MVC.Controllers {
  [OutputCache(NoStore = true, Duration = 0, VaryByParam = "*")]
  public class HomeController : Controller {
    public ActionResult Index() {
      ViewBag.Message = "Welcome to ASP.NET MVC!";

      return View();
    }

    public ActionResult About() {
      return View();
    }

    public ActionResult Example3editing() {
      return View();
    }
    public ActionResult Data() {
      return View();
    }

    #region Data

    #region WorkShiftMinutes
    public ActionResult WorkShiftMinutesGet(DateTimeOffset start) {
      Func<TimeCardEntitiesContainer, List<vWorkShiftMinute>> a = tc => {
        var list = tc.vWorkShiftMinutes.Where(wsm => wsm.WorkShiftStart == start).ToList();
        if (!list.Any())
          list.Add(new vWorkShiftMinute());
        return list;
      };
      return Json(a.Do(), JsonRequestBehavior.AllowGet);
    }
    #endregion

    #region WorkShift
    public ActionResult WorkShiftsGet() {
      Func<TimeCardEntitiesContainer, List<vWorkShift>> a = tc => {
        var list = tc.vWorkShifts.ToList();
        if (!list.Any())
          list.Add(new vWorkShift());
        return list;
      };
      return Json(a.Do(), JsonRequestBehavior.AllowGet);
    }
    #endregion

    #region PuncPair
    public ActionResult PunchPairsGet() {
      Func<TimeCardEntitiesContainer, List<vPunchPair>> a = tc => {
        var list = tc.vPunchPairs.ToList();
        if (!list.Any())
          list.Add(new vPunchPair());
        return list;
      };
      return Json(a.Do(), JsonRequestBehavior.AllowGet);
    }
    #endregion
    
    #region Punches
    public ActionResult PunchesGet() {
      return Json(new TimeCard.MVC.Models.TimeCardEntitiesContainer().vPunches.ToList(), JsonRequestBehavior.AllowGet);
    }
    public ActionResult PunchesAdd(Models.Punch punches) {
      Func<Models.TimeCardEntitiesContainer, Models.vPunch> a = tc => {
        tc.Punches.Add(punches);
        tc.SaveChanges();
        return tc.vPunches.Where(p => p.Time == punches.Time).First();
      };
      var res = a.Do();
      return Json(res);
    }
    public ActionResult PunchesUpdate(Models.Punch punches) {
      Func<Models.TimeCardEntitiesContainer, Models.vPunch> a = tc => {
        var punch = tc.Punches.Find(punches.Id);
        foreach (var p in  punch.GetType().GetProperties()) {
          if (!p.GetGetMethod().IsVirtual)
            p.SetValue(punch, p.GetValue(punches,null), null);
        }
        tc.SaveChanges();
        return tc.vPunches.Where(p => p.Id == punch.Id).First();
      };
      var res = a.Do();
      return Json(res);
    }
    public ActionResult PunchesDelete(Models.Punch punch) {
      Action<Models.TimeCardEntitiesContainer> a = tc => {
        tc.Punches.Remove(tc.Punches.Find(punch.Id));
      };
      a.Do();
      return Json(punch);
    }
    #endregion

    #region PunchDirections
    public ActionResult PunchDirectionsGet() {
      return Json(new TimeCard.MVC.Models.TimeCardEntitiesContainer().PunchDirections.ToList(), JsonRequestBehavior.AllowGet);
    }
    public ActionResult PunchDirectionsAdd(Models.PunchDirection punchDirection) {
      Action<Models.TimeCardEntitiesContainer> a = tc => tc.PunchDirections.Add(punchDirection);
      a.Do();
      return Json(punchDirection);
    }
    public ActionResult PunchDirectionsDelete(Models.PunchDirection punchDirection) {
      Action<Models.TimeCardEntitiesContainer> a = tc => {
        tc.PunchDirections.Remove(tc.PunchDirections.Find(punchDirection.Id));
      };
      a.Do();
      return Json(punchDirection);
    }
    #endregion

    #region RateCodeType
    public ActionResult RateCodeTypesGet() {
      return Json(new TimeCard.MVC.Models.TimeCardEntitiesContainer().vRateCodeTypes.ToList(), JsonRequestBehavior.AllowGet);
    }
    public ActionResult RateCodeTypesAdd(Models.RateCodeType RateCodeType) {
      Action<Models.TimeCardEntitiesContainer> a = tc => tc.RateCodeTypes.Add(RateCodeType);
      a.Do();
      return Json(RateCodeType);
    }
    public ActionResult RateCodeTypesUpdate(Models.RateCodeType rateCodeTypes) {
      Func<Models.TimeCardEntitiesContainer, Models.vRateCodeType> a = tc => {
        var rateCodeType = tc.RateCodeTypes.First(rcbr => rcbr.Id == rateCodeTypes.Id);
        CopyObject(rateCodeTypes, rateCodeType);
        tc.SaveChanges();
        return tc.vRateCodeTypes.First(p => p.Id == rateCodeType.Id);
      };
      var res = a.Do();
      return Json(res);
    }
    public ActionResult RateCodeTypesDelete(Models.RateCodeType RateCodeType) {
      Action<Models.TimeCardEntitiesContainer> a = tc => {
        tc.RateCodeTypes.Remove(tc.RateCodeTypes.Find(RateCodeType.Id));
      };
      a.Do();
      return Json(RateCodeType);
    }
    #endregion

    #region PunchType
    public ActionResult PunchTypesGet() {
      return Json(new TimeCard.MVC.Models.TimeCardEntitiesContainer().PunchTypes.ToList().DefaultIfEmpty(new PunchType()), JsonRequestBehavior.AllowGet);
    }
    public ActionResult PunchTypesAdd(Models.PunchType punchType) {
      Action<Models.TimeCardEntitiesContainer> a = tc => tc.PunchTypes.Add(punchType);
      a.Do();
      return Json(punchType);
    }
    public ActionResult PunchTypesDelete(Models.PunchType punchType) {
      Action<Models.TimeCardEntitiesContainer> a = tc => {
        tc.PunchTypes.Remove(tc.PunchTypes.Find(punchType.Id));
      };
      a.Do();
      return Json(punchType);
    }
    #endregion

    #region RateCode
    public ActionResult RateCodeByRangesGet() {
      return Json(new TimeCard.MVC.Models.TimeCardEntitiesContainer().vRateCodeByRanges.ToList(), JsonRequestBehavior.AllowGet);
    }
    public ActionResult RateCodeByRangesAdd(Models.RateCodeByRange rateCodeByRanges) {
      Action<Models.TimeCardEntitiesContainer> a = tc => tc.RateCodeByRanges.Add(rateCodeByRanges);
      a.Do();
      return Json(rateCodeByRanges);
    }
    public ActionResult RateCodeByRangesUpdate(Models.RateCodeByRange rateCodeByRanges) {
      Func<Models.TimeCardEntitiesContainer, Models.vRateCodeByRange> a = tc => {
        var rateCodeByRange = tc.RateCodeByRanges.First(rcbr => rcbr.Id == rateCodeByRanges.Id);
        CopyObject(rateCodeByRanges, rateCodeByRange);
        tc.SaveChanges();
        return tc.vRateCodeByRanges.First(p => p.Id == rateCodeByRange.Id);
      };
      var res = a.Do();
      return Json(res);
    }
    public ActionResult RateCodeByRangesDelete(Models.RateCodeByRange RateCodeByRanges) {
      Action<Models.TimeCardEntitiesContainer> a = tc => {
        tc.RateCodeByRanges.Remove(tc.RateCodeByRanges.Find(RateCodeByRanges.Id));
      };
      a.Do();
      return Json(RateCodeByRanges);
    }

    public ActionResult RateCodesGet() {
      return Json(new TimeCard.MVC.Models.TimeCardEntitiesContainer().vRateCodes.ToList(), JsonRequestBehavior.AllowGet);
    }
    public ActionResult RateCodesAdd(Models.RateCode rateCodes) {
      Func<Models.TimeCardEntitiesContainer, Models.vRateCode> a = tc => {
        tc.RateCodes.Add(rateCodes);
        tc.SaveChanges();
        return tc.vRateCodes.Single(rc => rc.Id == rateCodes.Id);
      };
      return Json(a.Do());
    }
    public ActionResult RateCodesUpdate(Models.RateCode rateCodes) {
      Func<Models.TimeCardEntitiesContainer, Models.vRateCode> a = tc => {
        var rateCode = tc.RateCodes.Find(rateCodes.Id);
        CopyObject(rateCodes, rateCode);
        tc.SaveChanges();
        return tc.vRateCodes.Single(p => p.Id == rateCode.Id);
      };
      var res = a.Do();
      return Json(res);
    }

    private static void CopyObject(object from, object to) {
      foreach (var p in to.GetType().GetProperties()) {
        if (!p.GetGetMethod().IsVirtual)
          p.SetValue(to, p.GetValue(from, null), null);
      }
    }
    public ActionResult RateCodesDelete(Models.RateCode rateCodes) {
      Action<Models.TimeCardEntitiesContainer> a = tc => {
        tc.RateCodes.Remove(tc.RateCodes.Find(rateCodes.Id));
      };
      a.Do();
      return Json(rateCodes);
    }
    #endregion

    #region RateCodeLayers
    public ActionResult RateCodeLayersGet() {
      return Json(new TimeCard.MVC.Models.TimeCardEntitiesContainer().RateCodeLayers.ToList().DefaultIfEmpty(new RateCodeLayer()), JsonRequestBehavior.AllowGet);
    }
    public ActionResult RateCodeLayersAdd(Models.RateCodeLayer RateCodeLayer) {
      Action<Models.TimeCardEntitiesContainer> a = tc => tc.RateCodeLayers.Add(RateCodeLayer);
      a.Do();
      return Json(RateCodeLayer);
    }
    public ActionResult RateCodeLayersUpdate(Models.RateCodeLayer RateCodeLayers) {
      Func<Models.TimeCardEntitiesContainer, Models.RateCodeLayer> a = tc => {
        var RateCodeLayer = tc.RateCodeLayers.First(c => c.Id == RateCodeLayers.Id);
        CopyObject(RateCodeLayers, RateCodeLayer);
        tc.SaveChanges();
        return tc.RateCodeLayers.First(p => p.Id == RateCodeLayer.Id);
      };
      return Json(a.Do());
    }
    public ActionResult RateCodeLayersDelete(Models.RateCodeLayer RateCodeLayer) {
      Action<Models.TimeCardEntitiesContainer> a = tc => {
        tc.RateCodeLayers.Remove(tc.RateCodeLayers.Find(RateCodeLayer.Id));
      };
      a.Do();
      return Json(RateCodeLayer);
    }
    #endregion

    #region RateCodeRules
    public ActionResult RateCodeRulesGet() {
      return Json(new TimeCard.MVC.Models.TimeCardEntitiesContainer().RateCodeRules.ToList().DefaultIfEmpty(new RateCodeRule()), JsonRequestBehavior.AllowGet);
    }
    public ActionResult RateCodeRulesAdd(Models.RateCodeRule RateCodeRule) {
      Action<Models.TimeCardEntitiesContainer> a = tc => tc.RateCodeRules.Add(RateCodeRule);
      a.Do();
      return Json(RateCodeRule);
    }
    public ActionResult RateCodeRulesUpdate(Models.RateCodeRule RateCodeRules) {
      Func<Models.TimeCardEntitiesContainer, Models.RateCodeRule> a = tc => {
        var RateCodeRule = tc.RateCodeRules.First(c => c.Id == RateCodeRules.Id);
        CopyObject(RateCodeRules, RateCodeRule);
        tc.SaveChanges();
        return tc.RateCodeRules.First(p => p.Id == RateCodeRule.Id);
      };
      return Json(a.Do());
    }
    public ActionResult RateCodeRulesDelete(Models.RateCodeRule RateCodeRule) {
      Action<Models.TimeCardEntitiesContainer> a = tc => {
        tc.RateCodeRules.Remove(tc.RateCodeRules.Find(RateCodeRule.Id));
      };
      a.Do();
      return Json(RateCodeRule);
    }
    #endregion


    #region Config
    public ActionResult ConfigsGet() {
      return Json(new TimeCard.MVC.Models.TimeCardEntitiesContainer().Configs.ToList().DefaultIfEmpty(new Config()), JsonRequestBehavior.AllowGet);
    }
    public ActionResult ConfigsAdd(Models.Config Config) {
      Action<Models.TimeCardEntitiesContainer> a = tc => tc.Configs.Add(Config);
      a.Do();
      return Json(Config);
    }
    public ActionResult ConfigsUpdate(Models.Config Configs) {
      Func<Models.TimeCardEntitiesContainer, Models.Config> a = tc => {
        var Config = tc.Configs.First(c => c.Id == Configs.Id);
        CopyObject(Configs, Config);
        tc.SaveChanges();
        return tc.Configs.First(p => p.Id == Config.Id);
      };
      return Json(a.Do());
    }
    public ActionResult ConfigsDelete(Models.Config Config) {
      Action<Models.TimeCardEntitiesContainer> a = tc => {
        tc.Configs.Remove(tc.Configs.Find(Config.Id));
      };
      a.Do();
      return Json(Config);
    }
    #endregion


    #region Work(Shift/Day)Rates
    public ActionResult WorkDayMinutesGet() {
      return Json(new TimeCard.MVC.Models.TimeCardEntitiesContainer().vWorkDayMinutes
        .OrderBy(wdm=>wdm.Date)
        .ToList(), JsonRequestBehavior.AllowGet);
    }
    public ActionResult WorkShiftRatesGet() {
      return Json(new TimeCard.MVC.Models.TimeCardEntitiesContainer().vWorkShiftRates.ToList(), JsonRequestBehavior.AllowGet);
    }
    public ActionResult WorkDayRatesGet() {
      var wdr = new TimeCard.MVC.Models.TimeCardEntitiesContainer().vWorkDayRates.ToList();
      return Json(wdr, JsonRequestBehavior.AllowGet);
    }
    #endregion

    #region Brealdown
    public ActionResult BuildMinuteBreakdownGet() {
      return Json(new TimeCard.MVC.Models.TimeCardEntitiesContainer().BuildMinuteBreakdown().ToList().DefaultIfEmpty(new BuildMinuteBreakdown_Result()), JsonRequestBehavior.AllowGet);
    }
    public ActionResult SalaryBreakdownsGet() {
      return Json(new TimeCard.MVC.Models.TimeCardEntitiesContainer().SalaryBreakdowns.ToList().DefaultIfEmpty(new SalaryBreakdown()), JsonRequestBehavior.AllowGet);
    }

    #endregion
    #endregion
  }
}
