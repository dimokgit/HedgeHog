﻿using System;
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

    public ActionResult Data() {
      return View();
    }

    #region Data
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