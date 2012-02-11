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
    public static TOutput Do<TOutput>(this Func<TimeCardEntitiesContainer,TOutput> func) {
      try {
        using (var context = new TimeCardEntitiesContainer()) {
          return func(context);
        }
      } catch {
        throw;
      }
    }
  }
}
