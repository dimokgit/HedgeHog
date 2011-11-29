CREATE VIEW dbo.v_TradeSession_10
AS
SELECT     TOP (100) PERCENT Pair, SessionId, MAX(TimeStamp) AS TimeStamp, COUNT(*) AS Count, SUM(GrossPL) AS GrossPL, DATEDIFF(dd, MIN(TimeOpen), 
                      MAX(TimeClose)) AS Days, MAX(Lot) AS Lot, AVG(Lot) AS LotA, STDEV(Lot) AS LotSD, SUM(GrossPL) / NULLIF (DATEDIFF(dd, MIN(TimeOpen), MAX(TimeClose)), 0) 
                      * 30.0 AS DollarsPerMonth, CONVERT(numeric(10, 2), AVG(PL)) AS PL, DATEDIFF(n, MIN(TimeStamp), MAX(TimeStamp)) AS MinutesInTest, CONVERT(float, 
                      DATEDIFF(dd, MIN(TimeOpen), MAX(TimeClose))) / NULLIF (DATEDIFF(n, MIN(TimeStamp), MAX(TimeStamp)), 0.0) AS DaysPerMinute, SessionInfo
FROM         dbo.t_Trade
WHERE     (IsVirtual = 1)
GROUP BY SessionId, Pair, SessionInfo
HAVING      (COUNT(*) >= 10)
ORDER BY TimeStamp DESC