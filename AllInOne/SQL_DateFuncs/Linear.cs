using System;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using Microsoft.SqlServer.Server;

[Serializable]
[Microsoft.SqlServer.Server.SqlUserDefinedAggregate(Format.Native)]
public struct Linear {
  private long count;
  private SqlDouble SumY;
  private SqlDouble SumXY;
  private SqlDouble SumX2;
  private SqlDouble SumY2;
  private SqlDouble SumX;

  public void Init() {
    count = 0;
    SumX = SumY = SumXY = SumX2 = SumY2 = 0;
  }

  public void Accumulate(SqlDouble y) {
    if(!y.IsNull) {
      count++;
      SumX += count;
      SumY += y;
      SumXY += count * y;
      SumX2 += count * count;
      SumY2 += y * y;
    }
  }

  public void Merge(Linear other) {
    count += other.count; SumY += other.SumY; SumXY += other.SumXY; SumX2 += other.SumX2; SumY2 += other.SumY2; SumX += other.SumX;
  }

  public SqlDouble Terminate() {
    if(count > 0) {
      SqlDouble value = (count * SumXY - (SumX * SumY)) / ((count * SumX2) - (SumX * SumX));
      return value;
    } else { return 0; }
  }
}
