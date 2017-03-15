using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Shared {
  public class LoginInfo {
    public string Account { get; set; }
    public string Password { get; set; }
    public bool IsDemo { get; set; }
    public bool Canceled { get; set; }
    public LoginInfo(string account, string password, bool isDemo) {
      this.Account = account;
      this.Password = password;
      this.IsDemo = isDemo;
    }
  }
}
