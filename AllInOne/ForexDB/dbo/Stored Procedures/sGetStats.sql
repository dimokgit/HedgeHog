CREATE PROCEDURE [dbo].[sGetStats]--'1/1/13',3
@DateStart datetime  = '3/1/13',
@FrameInPeriods int,
@Weeks int = 3
AS
SELECT TOP 100000 *
,R.Max RangeForTrade
, @DateStart StartDateMin
, MONTH(@DateStart) Month
FROM
(
SELECT WeekDay,DayName
, MIN(StartDate) StartDate
, AVG(Range)Range
,STDEV(Range)RangeSD
, MIN(Range)RangeMin
,AVG(Range) - STDEV(Range) RangeMinSD
,COUNT(*) Count
FROM [Stats]('EUR/JPY',1,@DateStart,@FrameInPeriods,24*60*6*@Weeks) 
WHERE StartDate < DATEADD(wk,@Weeks,@DateStart)
GROUP BY WeekDay,DayName
)T
CROSS APPLY MaxNum(RangeMin*1.1,RangeMinSD)R
ORDER BY WeekDay