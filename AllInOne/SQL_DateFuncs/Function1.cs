using System;
using System.Data.SqlTypes;
using Microsoft.SqlServer.Server;
using System.Collections;
using System.Text.RegularExpressions;

public partial class UserDefinedFunctions {
  [Microsoft.SqlServer.Server.SqlFunction]
  public static DateTimeOffset ToDateTimeOffset(DateTime date) {
    return new DateTimeOffset(date);
  }
  [SqlFunction(FillRowMethodName="FillSplitRow",TableDefinition="Value nvarchar(4000)")]
  public static IEnumerable clrSplit(SqlString text, SqlString separator) {
    var s = separator.IsNull ? "," : separator.Value;
    return  (s.Length == 1 ? text.Value.Split(s[0]) : Regex.Split(text.Value, s));
  }
  public static void FillSplitRow(object o, out SqlString value) {
    value = new SqlString(o.ToString());
  }
  [SqlFunction(FillRowMethodName = "FillSplitTwoRow", TableDefinition = "Value1 nvarchar(2000),Value2 nvarchar(2000)")]
  public static IEnumerable clrSplitTwo(SqlString text, SqlString separator) {
    var s = separator.IsNull ? "," : separator.Value;
    return new[] { (s.Length == 1 ? text.Value.Split(s[0]) : Regex.Split(text.Value, s)) };
  }
  public static void FillSplitTwoRow(object o, out SqlString value1, out SqlString value2) {
    var split = o as string[];
    value1 = new SqlString(split.Length > 0 ? split[0] : "");
    value2 = new SqlString(split.Length > 1 ? split[1] : "");
  }

  static SqlDouble GetSlope(double[] yArray) {
    if(yArray == null)
      throw new ArgumentNullException("yArray", "Is null");
    if(yArray.Length == 0)
      return SqlDouble.Null;
    if(yArray.Length == 1) {
      return 0;
    }
    double n = yArray.Length;
    double sumxy = 0, sumx = 0, sumy = 0;
    double sumx2 = 0;
    for(int i = 0; i < n; i++) {
      sumxy += i * yArray[i];
      sumx += i;
      sumy += yArray[i];
      sumx2 += (long)i * i;
    }
    return ((sumxy - sumx * ( sumy / n)) / (sumx2 - sumx * sumx / n));
  }

};

