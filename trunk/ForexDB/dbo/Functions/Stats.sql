CREATE FUNCTION [dbo].[Stats](
  @Pair sysname,
  @Period int,
  @StartDateLocal datetime,
  @StatsRange int,
  @Periods int
)RETURNS TABLE AS
RETURN 
WITH Stats AS
(
SELECT b.StartDateLocal StartDate
      ,b.AskClose
      ,STDEV(b2.AskClose) StDev
      ,MIN(b2.AskClose) Min
      ,MAX(b2.AskClose) Max
      ,AVG(b2.PriceHeight)PriceHeight
      ,STDEV(b2.PriceHeight)PriceHeightSD
FROM t_Bar b
CROSS APPLY
(
SELECT TOP (@StatsRange) AskClose,AskHigh-BidLow PriceHeight,StartDate FROM t_Bar b1
WHERE b1.Pair = b.Pair AND b1.Period = b.Period AND b1.StartDate <= b.StartDate
ORDER BY b1.StartDate DESC
)b2(AskClose,PriceHeight,SD)
WHERE Pair = @Pair AND StartDateLocal > @StartDateLocal AND Period = @Period
GROUP BY b.StartDateLocal,b.AskClose
), Stats1 AS
(
SELECT TOP (@Periods) *
,Max-Min Range
,StDev/(Max-Min) Volatility
FROM Stats
ORDER BY StartDate
)
SELECT *
,CAST(StartDate as date) StartDate1
,DATEPART(dw,StartDate)WeekDay
,DATENAME(dw,StartDate)DayName
,DATEPART(m,StartDate)Month
,(PriceHeight/Range)/PriceHeightSD Voltage
FROM Stats1