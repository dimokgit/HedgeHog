CREATE PROCEDURE [dbo].[BarsByMinutes]
	@Pair varchar(7),
	@Period tinyint,
	@DateEnd datetime,
	@BarMinutes int,
	@BarsCount int
AS

WITH DayBars AS(
SELECT *,1 Row FROM Bar(@Pair,@Period,DATEADD(n,-@Period,@DateEnd),@BarMinutes)
UNION ALL
SELECT B.*,Row+1 FROM DayBars DB
CROSS APPLY Bar(@Pair,@Period,DATEADD(n,-@Period,DB.DateOpen),@BarMinutes) B
WHERE Row < @BarsCount*1.5
)


SELECT DISTINCT TOP(@BarsCount)
AskHigh, AskLow, BidHigh, BidLow, AskOpen, AskClose, BidOpen, BidClose,  DateOpen,DateClose,
DATEDIFF(hh,DateClose, DateOpen )Hours,
(AskHigh+BidHigh-AskLow-BidLow)/2 Spread
FROM DayBars ORDER BY DateOpen DESC
option (maxrecursion 32767);
