using System;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using Microsoft.SqlServer.Server;
using SQLCLR;

public partial class UserDefinedFunctions {

  #region DateTime
  [Microsoft.SqlServer.Server.SqlFunction]
  public static SqlDateTime RoundToMinute(SqlDateTime date,SqlByte period) {
    return date.IsNull ? SqlDateTime.Null: date.Value.Round(period.IsNull ? (byte)1 : period.Value);
  }
  [Microsoft.SqlServer.Server.SqlFunction]
  public static SqlDateTime Date(SqlDateTime date) {
    return date.IsNull ? SqlDateTime.Null : date.Value.Date;
  }
  [Microsoft.SqlServer.Server.SqlFunction]
  public static SqlDateTime Time(SqlDateTime date) {
    return date.IsNull ? SqlDateTime.Null : new DateTime(date.TimeTicks);
  }
  #endregion

};

