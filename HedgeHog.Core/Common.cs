using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class Common {
    public static string CurrentDirectory { get { return AppDomain.CurrentDomain.BaseDirectory; } }
    public static string ExecDirectory() {
      return CurrentDirectory.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Last();
    }
    public static IEnumerable<string> LoadText(string path) {
      yield return
      Uri.IsWellFormedUriString(path, UriKind.Absolute)
      ? DownloadText(new Uri(path))
      : File.ReadAllText(AbsolutePath(path));
    }

    public static string AbsolutePath(string path) =>
      Path.IsPathRooted(path) ? path : Path.Combine(Common.CurrentDirectory, path);

    internal static string DownloadText(Uri url) {
      using(var wc = new WebClient())
        return wc.DownloadString(url);
    }
    public static string CallerChain(string caller, [CallerMemberName] string Caller = "") => $"{Caller}<={caller}";
  }
}
