using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.DB {
  public static class ForexStorage {
    static ForexEntities ForexEntitiesFactory() { return new ForexEntities() { CommandTimeout = 60 * 1 }; }
    public static void UseForexContext(Action<ForexEntities> action,  Action<ForexEntities> exit = null) {
      UseForexContext(action, null, exit);
    }
    public static void UseForexContext(Action<ForexEntities> action, Action<ForexEntities, Exception> error = null, Action<ForexEntities> exit = null, int commandTimeout = 0) {
      using (var context = ForexEntitiesFactory())
        try {
          if (commandTimeout > 0)
            context.CommandTimeout = commandTimeout;
          action(context);
          if (exit != null) exit(context);
        } catch (Exception exc) {
          if (error != null) error(context, exc);
          else {
            GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(exc);
            throw;
          }
        }
    }
    public static T UseForexContext<T>(Func<ForexEntities, T> action, Action<ForexEntities, Exception> error = null, Action<ForexEntities> exit = null) {
      try {
        using (var context = ForexEntitiesFactory()) {
          try {
            context.CommandTimeout = 60 * 1;
            return action(context);
          } catch (Exception exc) {
            if (error != null) {
              error(context, exc);
              return default(T);
            } else throw;
          }
        }
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(exc);
        throw;
      }
    }

  }
}
