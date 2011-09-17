CREATE FUNCTION [dbo].[CorridorAverage](
	@Pair varchar(7),
	@Period tinyint,
	@CorridorDate datetime,
	@CorridorPeriods int,
	@BarMinutes int
)RETURNS TABLE AS
RETURN(
WITH SPREADS(StartDate,Spread)AS(
SELECT TOP (@CorridorPeriods)StartDate,(C.AskHigh+C.BidHigh-C.AskLow-C.BidLow)/2 Spread
FROM t_Bar B CROSS APPLY Bar(@Pair,@Period,StartDate,@BarMinutes)C
WHERE B.Pair = @Pair AND B.Period = @Period AND StartDate <= @CorridorDate ORDER BY StartDate DESC
)

SELECT AVG(Spread)Avg, STDEV(Spread)StDev FROM(
SELECT Spread FROM SPREADS 
--WHERE Spread <= (SELECT AVG(Spread) FROM SPREADS 
WHERE Spread <= (SELECT AVG(Spread) FROM SPREADS)
--)
)T
)
