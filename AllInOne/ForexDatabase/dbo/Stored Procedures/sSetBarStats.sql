CREATE PROCEDURE [dbo].[sSetBarStats] --@StartDate = '12/1/2011'
  @Pair varchar(30) ='USDOLLAR',
  @Period int = 1,
  @Length int = 720,
  @StartDate datetimeoffset,-- = DATEADD(wk,-10,GETDATE())-- '2012-09-02 17:01:00.000'
  @StopDate datetimeoffset  = '1/1/9999'--= DATEADD(wk, 1,@StartDate)
AS SET NOCOUNT ON

SELECT @StartDate = MAX(StartDate) FROM
(
SELECT MIN(StartDate)StartDate FROM t_Bar B WHERE B.Pair = @Pair AND B.Period = @Period AND B.StartDate >= ISNULL(@StartDate,'1/1/1900')
UNION ALL
SELECT @StartDate
)T

SELECT @StopDate = MIN(StartDate) FROM
(
SELECT MAX(StartDate)StartDate FROM t_Bar B WHERE B.Pair = @Pair AND B.Period = @Period AND B.StartDate <= ISNULL(@StopDate,'1/1/9999')
UNION ALL
SELECT @StopDate
)T

PRINT 'Setting from '+CAST(@StartDate AS varchar)+' to '+CAST(@StopDate AS varchar)

DELETE FROM t_BarStats WHERE Pair = @Pair AND Period = @Period AND Length = @Length AND StartDate BETWEEN @StartDate AND ISNULL(@StopDate,'1/1/9999')

EXEC s_SetBarRows @Pair,@Period

DECLARE @I TABLE(StartDate datetimeoffset,[Count] int)

WHILE @StartDate <= @StopDate BEGIN

INSERT INTO t_BarStats(Pair,Period,Length,StartDate,StopDate,BarsHeight,PriceStDev,Distance,PriceHeight)
OUTPUT inserted.StartDate,inserted.Length INTO @I
SELECT @Pair,@Period,Count,StartDate, StopDate, BarsHeight,PriceStDev,Distane,PriceHeight
FROM dbo.GetBarStats(@Pair,@Period,@Length,@StartDate)

IF @@ERROR <> 0 BREAK

IF EXISTS(SELECT * FROM @I WHERE Count < @Length) BREAK

SELECT @StartDate = DATEADD(n,1,StartDate) FROM @I
DELETE FROM @I

END
--- UnitTest
--DECLARE @Start datetimeoffset = dbo.todatetimeoffset('1/3/2012')
--DECLARE @Stop datetimeoffset = dbo.todatetimeoffset('1/14/2012 3:59')
--EXEC sSetBarStats 'USDOLLAR',1,240,@Start,@Stop
