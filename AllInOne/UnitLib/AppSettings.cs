namespace UnitLib {
  public class AppSettings :HedgeHog.AppSettingsBase<AppSettings> {
    public static string MongoUri => Required();
    public static string Secret => Value();
    public static int SecretCount => Required<int>();
    public static int SecretRequiredMissing => Required<int>();
  }
}
