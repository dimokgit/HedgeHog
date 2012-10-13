using System;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using Microsoft.SqlServer.Server;

public partial class UserDefinedFunctions {
  [Microsoft.SqlServer.Server.SqlFunction]
  public static DateTimeOffset ToDateTimeOffset(DateTime date) {
    return new DateTimeOffset(date);
  }
};

