using System;

namespace TimeCard.MVC.Models {
  public static class TimeCardEntitiesExt {
    public static void Do(this Action<TimeCardEntitiesContainer> action,bool save = true) {
      try {
        using (var context = new TimeCardEntitiesContainer()) {
          action(context);
          if (save) context.SaveChanges();
        }
      } catch {
        throw;
      }
    }
  }
}
