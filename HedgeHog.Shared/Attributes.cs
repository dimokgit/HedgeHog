using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  // Do Not Recessitate
  [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
  public sealed class DnrAttribute : Attribute { }
  public sealed class IsNotStrategyAttribute : Attribute {  }
  [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
  public class WwwAttribute : Attribute {
    public string Group { get; set; } = "";
    public WwwAttribute(string group) {
      Group = group;
    }
    public WwwAttribute() {

    }
  }
  public class WwwSettingAttribute : WwwAttribute {
    public bool Hide { get; set; } = false;
    public WwwSettingAttribute() : base() {
    }
    public WwwSettingAttribute(string group) : base(group) {
      Hide = false;
    }
    public WwwSettingAttribute(string group,bool hide) : base(group) {
      Hide = hide;

    }
  }
  public class WwwInfoAttribute : WwwAttribute {
    public WwwInfoAttribute() : base() {
    }
    public WwwInfoAttribute(string group) : base(group) {

    }
  }
}
