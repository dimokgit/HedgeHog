CREATE PROCEDURE sAnalitycs AS
WITH PAIR0 AS
(
select StartDate,((AskClose+BidClose)/2) Price
from t_Bar where Pair = 'SPY' and Period = 3
), PAIR1 AS
(
select StartDate,
AVG(Price) OVER(ORDER BY startdate ROWS 21 PRECEDING) Price
from PAIR0
),PAIR AS
(
select CAST(StartDate AS date) StartDate, Price,
LAG(Price) OVER(ORDER BY startdate) PricePrev
from PAIR1
), PG AS
(
SELECT StartDate, AVG(Price) Price FROM PAIR GROUP BY StartDate
), UP AS
(
SELECT StartDate
, STDEV(LOG(Price/PricePrev))  StdUp
FROM PAIR
WHERE Price > PricePrev
GROUP BY StartDate
HAVING COUNT(*) > 100
), DW AS
(
SELECT StartDate
, STDEV(LOG(Price/PricePrev))  StdDown
FROM PAIR
WHERE Price < PricePrev
GROUP BY StartDate
HAVING COUNT(*) > 100
), T AS
(
SELECT PG.StartDate Date,PG.Price
,UP.StdUp
,DW.StdDown
FROM PG
OUTER APPLY (SELECT TOP 1 * FROM UP U WHERE U.StartDate <= PG.StartDate AND U.StdUp <>0 ORDER BY U.StartDate DESC)UP
OUTER APPLY (SELECT TOP 1 * FROM DW U WHERE U.StartDate <= PG.StartDate AND U.StdDown <>0 ORDER BY U.StartDate DESC)DW
)
SELECT *,Price/PricePrev FROM PAIR
--SELECT AVG(StdUp/StdDown) Ratio
--FROM T
--WHERE StdUp/StdDown BETWEEN 0.5 AND 1.5
