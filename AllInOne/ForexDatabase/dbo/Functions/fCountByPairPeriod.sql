CREATE FUNCTION [dbo].[fCountByPairPeriod](
@Pair varchar(10) ='ESH9',@Period int = 3
)RETURNS TABLE AS RETURN
WITH T AS
(
SELECT Pair,Period,CAST(StartDate AT TIME ZONE 'Eastern Standard Time' AS date)Date
FROM t_Bar 
WHERE Pair = @Pair AND Period = @Period
)
SELECT @Pair Pair,@Period Period,Date, COUNT(*) Cout
FROM T
GROUP BY Date
