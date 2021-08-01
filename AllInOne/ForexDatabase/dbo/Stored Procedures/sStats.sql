CREATE PROCEDURE sStats AS
IF 1= 0 BEGIN
DROP TABLE #T
DECLARE @TimeFrame int = 120
;WITH B AS
(
SELECT   StartDate, AskHigh, BidLow, BidOpen
FROM            t_Bar BP
WHERE        (Pair = N'XAUUSD') AND (Period = 1) AND StartDate BETWEEN '20170206' AND '20170808'
), T0 AS
(
SELECT StartDate,BMAX.Count, BMAX.DateMax,BMIN.DateMin,BMAX.AskHigh,BMIN.BidLow, (DATEDIFF(n,BMIN.DateMin,BMAX.DateMax))Minutes
FROM B BP
CROSS APPLY(SELECT TOP 1 COUNT(*)OVER(PARTITION BY BP.StartDate)Count , StartDate DateMax,AskHigh FROM B WHERE DATEDIFF(n,BP.StartDate,StartDate) BETWEEN 0 AND @TimeFrame ORDER BY AskHigh DESC)BMAX
CROSS APPLY(SELECT TOP 1 StartDate DateMin,BidLow FROM B WHERE DATEDIFF(n,BP.StartDate,StartDate) BETWEEN 0 AND @TimeFrame ORDER BY BidLow)BMIN
), T1 AS
(
SELECT  *,SIGN(Minutes) Sign FROM T0
)
SELECT ROW_NUMBER() OVER(ORDER BY StartDate)Row,* INTO #T FROM T1 WHERE Count > @TimeFrame ORDER BY Row
CREATE CLUSTERED INDEX IDX_ROW ON #T(Row)

SELECT * FROM #T ORDER BY Row /*DROP TABLE #T*/ RETURN END 

SELECT DATEPART(HH,StartDate) Hour, AVG(AskHigh-BidLow) Height
FROM #T
GROUP BY DATEPART(HH,StartDate)
ORDER BY DATEPART(HH,StartDate)

RETURN
;WITH T AS
(
SELECT 1 Seq, Row,Count,DateMax,DateMin,Minutes,Sign,AskHigh,BidLow FROM #T WHERE Row = 1
UNION ALL
SELECT T.Seq+ IIF(T.Sign=T1.Sign,0,1),T1.Row,T1.Count,T1.DateMax,T1.DateMin,T1.Minutes,T1.Sign,T1.AskHigh,T1.BidLow
FROM #T T1 INNER JOIN T ON T1.Row=T.Row+1
), T1 AS
(
SELECT *
FROM T
CROSS APPLY(SELECT MAX(Date)DateMax2,MIN(Date)DateMin2 FROM(SELECT DateMax Date UNION ALL SELECT DateMin)MM)MM
), T2 AS
(
SELECT Seq,MIN(Count)Count,MAX(DateMax2)DateMax,MIN(DateMin2)DateMin,MAX(AskHigh)AskHihg,MIN(BidLow) BidLow,Sign
FROM T1 T
GROUP BY Seq,Sign
), T3 AS
(
SELECT 
	CAST(DateMin AS date)Date, DATEPART(hh,DateMin) Hour,
Count, DateMin DateStart,DATEDIFF(n,DateMin,DateMax)Minutes,AskHihg-BidLow Height,Sign
FROM T2 T 
)
--SELECT * FROM T

SELECT *, Minutes*Height 
FROM T3 T
ORDER BY DateStart
option (maxrecursion 0)

--GROUP BY BP.StartDate
--ORDER BY BP.StartDate
--DROP TABLE #T
