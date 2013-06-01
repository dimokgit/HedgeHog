CREATE FUNCTION stats.fHourly(
  @Pair varchar(10),
  @Period int,
  @DateStartLocal datetime,
  @Range int
)RETURNS TABLE AS RETURN
WITH S AS
(
SELECT        StartDate, AskClose, StDev, Min, Max, Range, Volatility
, DATEPART(hour,StartDate)Hour
FROM            dbo.Stats(@Pair, @Period,@DateStartLocal, @Range, 2147483647)
), S1 AS
(
SELECT Hour,AVG(StDev)StDev,STDev(StDev)StDevSD,AVG(Range)Range,STDEV(Range)RangeSD,COUNT(*)Count
FROM S
GROUP BY Hour
)
SELECT TOP 100 *,StDevSD/StDev StFevRatio FROM S1
ORDER BY Hour
--SELECT * FROM stats.fHourly('EUR/JPY',1,'3/10/13',60)