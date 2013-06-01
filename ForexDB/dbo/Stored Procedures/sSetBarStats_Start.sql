CREATE PROCEDURE [dbo].[sSetBarStats_Start] --@StartDate = '12/1/2011'
  @Pair varchar(30) ='USDOLLAR',
  @Period int = 1,
  @Length int = 720,
  @StartDate datetimeoffset,-- = DATEADD(wk,-10,GETDATE())-- '2012-09-02 17:01:00.000'
  @StopDate datetimeoffset  = '1/1/9999'--= DATEADD(wk, 1,@StartDate)
AS SET NOCOUNT ON

DECLARE @SD datetimeoffset = '1/1/9999'

SELECT @SD = MIN(StartDate) FROM t_BarStats WHERE Pair = @Pair AND Period = @Period AND Length = @Length
EXEC sSetBarStats @Pair,@Period,@Length,@StartDate,@SD

SELECT @SD = MAX(StartDate) FROM t_BarStats WHERE Pair = @Pair AND Period = @Period AND Length = @Length
EXEC sSetBarStats @Pair,@Period,@Length,@SD,@StopDate