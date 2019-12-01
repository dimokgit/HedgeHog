using System;
using System.Linq;

namespace HedgeHog {
  public class AppSettings :AppSettingsBase<AppSettings> {
    public static string MongoUri => Required();
    public static string SmsEmailAddress => Required();
    public static string SmsEmailPassword => Required();
    public static string SmsTradeConfirmation => Required();
  }
}
