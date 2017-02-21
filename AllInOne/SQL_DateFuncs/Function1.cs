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
};

