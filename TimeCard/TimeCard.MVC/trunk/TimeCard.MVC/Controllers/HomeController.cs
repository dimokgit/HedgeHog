using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Diagnostics;
using TimeCard.MVC.Models;

namespace TimeCard.MVC.Controllers {
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
        tc.Punches.Remove(tc.Punches.Find(punch.Time));
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
    #region PunchType
    public ActionResult PunchTypesGet() {
      return Json(new TimeCard.MVC.Models.TimeCardEntitiesContainer().PunchTypes.ToList(), JsonRequestBehavior.AllowGet);
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
    #endregion
  }
}
