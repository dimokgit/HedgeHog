CREATE FUNCTION fOpenCloseDiff2(
--DECLARE
@Pair varchar(10)='ESH9',
@Priod int = 3,
@Hour int = 15
)RETURNS TABLE AS RETURN
WITH D0 AS
(
SELECT --TOP 10000000000
 (AskOpen+BidOpen)/2 Price
,CAST(StartDate AT TIME ZONE 'US Eastern Standard Time' AS datetime) StartDate
FROM t_Bar
WHERE Pair=@Pair AND Period = @Priod AND StartDate > DATEADD(mm,-4,GETDATE())

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
--,Slope.Slope
--,Slope.PriceMax
--,Slope.PriceMin
--,(Slope.PriceMax+Slope.PriceMin)/2PriceAvg
FROM D1 D
--CROSS APPLY (SELECT -dbo.Linear(Price) Slope,MAX(D1.Price) PriceMax,MIN(D1.Price) PriceMin FROM D1 WHERE D1.Date = D.Date AND D.Hour = 9 AND D.Minute = 57 AND D1.Hour BETWEEN 5 AND 9) Slope
), D3 AS
(
SELECT 
ROW_NUMBER() OVER(ORDER BY StartDate DESC) Row,
*
FROM D2 D
WHERE D.Minute = 57
), H AS
(
--SELECT 9 H 
--UNION
--SELECT 10 H 
--UNION
--SELECT 11 H 
--UNION
--SELECT 12 H 
--UNION
--SELECT 13 H 
--UNION
--SELECT 14 H 
--UNION
SELECT 15 H 
)
, D4 AS
(
SELECT 
--IIF(D.Slope > 0,D.PriceAvg-D2.Price,D2.Price-D.priceAvg) DiffSlope,
AVG(ABS(D.Price-D2.Price)) 
	OVER(ORDER BY D.StartDate DESC
		ROWS BETWEEN CURRENT ROW AND 22 FOLLOWING) DiffAvg,
STDEV(ABS(D.Price-D2.Price)) 
	OVER(ORDER BY D.StartDate DESC
		ROWS BETWEEN CURRENT ROW AND 22 FOLLOWING) DiffStd,
ABS(D.Price-D2.Price) Diff,
D.Price,D2.Price Price2,D.StartDate,D2.StartDate EndDate
, H.H
--,D.Slope,
--D.PriceMax,
--D.PriceMin
FROM D3 D
INNER JOIN H ON D.Hour = H.H
CROSS APPLY(
SELECT * FROM D3 D2 WHERE D.Hour = H.H AND D.Date < D2.Date AND D2.Hour=H.H 
ORDER BY D2.StartDate OFFSET (1) ROWS FETCH NEXT (1) ROWS ONLY
) D2
--INNER JOIN #D D3 ON D.Date = DATEADD(D2.Date AND D.Hour = 10 AND D2.Hour=16
--INNER JOIN #D D2 ON D.Date
)
SELECT 
--AVG(D.DiffSlope) 
--	OVER(ORDER BY D.StartDate DESC
--		ROWS BETWEEN CURRENT ROW AND 22 FOLLOWING) DiffSlopeAvg
--,
/*
D.*
*/
H,AVG(D.DiffAvg) DiffAvg,stdev(D.DiffAvg) StdAvg
FROM D4 D
GROUP BY H
