
CREATE PROCEDURE [dbo].[sSetBarHeights] --@StartDate = '12/1/2011'
  @Pair varchar(30) ='EUR/JPY',
  @Period int = 1,
  @Length int = 720,
  @StartDate datetimeoffset ='12/1/2011',
  @StopDate datetimeoffset  ='9/1/2012'
AS
WHILE @StartDate < @StopDate BEGIN

IF NOT EXISTS(SELECT * FROM t_BarHeight WHERE Pair = @Pair AND Period = @Period AND StartDate = @StartDate)
  INSERT INTO t_BarHeight
  SELECT @Pair Pair,@Period Period,@Length [Length], @StartDate StartDate, AVG(Height)AvgHeight, STDEV(Height)StDevHeight
  --INTO t_BarHeight
  FROM
  (
  SELECT DISTINCT dbo.GetBarsHeight(@Length,Pair,StartDate) Height
  FROM t_Bar
  WHERE Pair = @Pair AND Period = @Period AND StartDate BETWEEN @StartDate AND DATEADD(mm,1,@StartDate)
  )T

SET @StartDate = DATEADD(mm,1,@StartDate)

END