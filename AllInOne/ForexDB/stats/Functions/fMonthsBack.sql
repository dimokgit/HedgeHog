CREATE FUNCTION stats.fMonthsBack(
  @Pair varchar(10),
  @Period int,
  @StatsRange int,
  @EndDate date,
  @MonthsBack int
)RETURNS TABLE
RETURN
WITH M AS
(
SELECT 
DATEDIFF(d,StartDate,@EndDate)  / DAY(EOMONTH(StartDate)) Month,
 * FROM Stats(@Pair,@Period,DATEADD(month,-@MonthsBack,@EndDate),@StatsRange,1440000000)
)
SELECT TOP (@MonthsBack)
  Month
, AVG(StDev) StDevAvg
, STDEV(StDev) StDevStDev
, COUNT(*) Count
, DATEADD(month,-Month,@EndDate) Date
FROM M
GROUP BY Month
ORDER BY Month
/*
WITH Dates AS
(
SELECT CAST(GETDATE() AS date) Date
UNION ALL
SELECT DATEADD(d,-1,Date) FROM Dates WHERE DATEDIFF(d,Date,GETDATE())<= 5
)
SELECT S.* FROM Dates
CROSS APPLY stats.fMonthsBack('EUR/JPY',1,1440,Dates.Date,1)S
*/
--SELECT * 
--INTO stats.MonthlyStats
--FROM stats.fMonthsBack('EUR/JPY',1,1440,EOMONTH(GETDATE()),16)
----DROP TABLE stats.MonthlyStats
