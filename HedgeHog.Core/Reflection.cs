using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class ReflectionCore {
    public static void SetProperty<T>(this object o, string p, T v) {
      System.Reflection.PropertyInfo pi = o.GetType().GetProperty(p);
      if(pi != null)
        pi.SetValue(o, v, new object[] { });
      else {
        System.Reflection.FieldInfo fi = o.GetType().GetField(p);
        if(fi == null)
          throw new NotImplementedException("Property " + p + " is not implemented in " + o.GetType().FullName + ".");
        fi.SetValue(o, v);
      }
    }


    public static void SetProperty(this object o, string p, object v) {
      if(o == null)
        throw new NullReferenceException(new { o, p, v } + "");
      o.SetProperty(p, v, pi => pi.GetSetMethod() != null || pi.GetSetMethod(true) != null);
    }
    public static void SetProperty(this object o, string p, object v, Func<PropertyInfo, bool> propertyPredicate = null) {
      var convert = new Func<object, Type, object>((value, type) => {
        if(value != null) {
          Type tThis = Nullable.GetUnderlyingType(type);
          var isNullable = true;
          if(tThis == null) {
            tThis = type;
            isNullable = false;
          }
          if(tThis.IsEnum)
            try {
              return Enum.Parse(tThis, v + "", true);
            } catch(Exception exc) {
              throw new ArgumentException(new { property = p } + "", exc);
            }
          return string.IsNullOrWhiteSpace((v ?? "") + "") && isNullable ? null : Convert.ChangeType(v, tThis, null);
        }
        return value;
      });
      var t = o.GetType();
      var pi = t.GetProperty(p);
      if(pi == null) {
        pi = t.GetProperties().FirstOrDefault(prop => prop.GetCustomAttributes<DisplayNameAttribute>().Any(dn => dn.DisplayName == p));
      }
      if(propertyPredicate != null) {
        if(pi == null)
          throw new MissingMemberException(t.Name, p);
        if(!propertyPredicate(pi))
          return;
      }
      if(pi != null)
        pi.SetValue(o, v = convert(v, pi.PropertyType), new object[] { });
      else {
        System.Reflection.FieldInfo fi = o.GetType().GetField(p);
        if(fi == null)
          throw new MissingMemberException(t.Name, p);
        fi.SetValue(o, convert(v, fi.FieldType));
      }
    }
  }
}
