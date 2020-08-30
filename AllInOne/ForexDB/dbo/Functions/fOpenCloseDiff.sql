CREATE FUNCTION fOpenCloseDiff(
--DECLARE
@Pair varchar(10)='ESH9',
@Priod int = 3,
@Hour int = 9
)RETURNS TABLE AS RETURN
WITH D0 AS
(
SELECT TOP 10000000000
 (AskOpen+BidOpen)/2 Price
,CAST(StartDate AT TIME ZONE 'US Eastern Standard Time' AS datetime) StartDate
FROM t_Bar
WHERE Pair=@Pair AND Period = @Priod AND StartDate > DATEADD(mm,-4,GETDATE())
ORDER BY StartDate DESC
), D1 AS
(
SELECT D.*
,CAST(StartDate AS date) Date
,DATEPART(hh,StartDate) Hour
,DATEPART(mi,StartDate) Minute
FROM D0 D
--GROUP BY Pair,StartDate
), D2 AS
(
SELECT D.*
,Slope.Slope
,Slope.PriceMax
,Slope.PriceMin
,(Slope.PriceMax+Slope.PriceMin)/2PriceAvg
FROM D1 D
CROSS APPLY (SELECT -dbo.Linear(Price) Slope,MAX(D1.Price) PriceMax,MIN(D1.Price) PriceMin FROM D1 WHERE D1.Date = D.Date AND D.Hour = 9 AND D.Minute = 57 AND D1.Hour BETWEEN 5 AND 9) Slope
), D3 AS
(
SELECT *
FROM D2 D
WHERE D.Minute = 57
), D4 AS
(
SELECT 
IIF(D.Slope > 0,D.PriceAvg-D2.Price,D2.Price-D.priceAvg) DiffSlope,
AVG(ABS(D.Price-D2.Price)) 
	OVER(ORDER BY D.StartDate DESC
		ROWS BETWEEN CURRENT ROW AND 22 FOLLOWING) DiffAvg,
STDEV(ABS(D.Price-D2.Price)) 
	OVER(ORDER BY D.StartDate DESC
		ROWS BETWEEN CURRENT ROW AND 22 FOLLOWING) DiffStd,
D.Price,D2.Price Price2,D.StartDate,D2.StartDate EndDate,
D.Slope,
D.PriceMax,
D.PriceMin
FROM D3 D
INNER JOIN D3 D2 ON D.Hour = @Hour AND D.Date = D2.Date AND D2.Hour=15
--INNER JOIN #D D3 ON D.Date = DATEADD(D2.Date AND D.Hour = 10 AND D2.Hour=16
--INNER JOIN #D D2 ON D.Date
)
SELECT TOP 100000000 
AVG(D.DiffSlope) 
	OVER(ORDER BY D.StartDate DESC
		ROWS BETWEEN CURRENT ROW AND 22 FOLLOWING) DiffSlopeAvg
,
D.*
--SELECT AVG(D.Diff) Diff,STDEV(D.Diff) ,AVG(D.DiffAvg) DiffAvg,stdev(D.DiffAvg) DiffAvg
FROM D4 D
ORDER BY D.StartDate DESC
