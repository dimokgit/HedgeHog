CREATE PROCEDURE [dbo].[sClearBars] AS
DECLARE @Pair varchar(10)='ESM0',--'ESH9',--RTYM9',--ESH9',
@Priod int = 3,
@goto int = 1
--DELETE t_Bar WHERE @Pair = Pair AND Period = @Priod AND CAST(StartDate AS Date)= '2019-07-09' 
IF @goto = 2 GOTO DELETE_PAIR
IF @goto = 3 GOTO UPDATE_PAIR
IF @goto = 0 GOTO  SHOW_PAIRS
IF @goto = 1 GOTO SHOW_PAIR
RETURN

SHOW_PAIRS:
SELECT Pair,Period,MAX(StartDate) AT TIME ZONE 'US Eastern Standard Time' StartDateMax,MIN(StartDate) AT TIME ZONE 'US Eastern Standard Time' StartDateMin,COUNT(*) Count FROM t_Bar GROUP BY Pair,Period
RETURN

SHOW_PAIR:
;WITH D AS
(
SELECT Pair,Period,CAST(StartDate AT TIME ZONE 'US Eastern Standard Time' AS date)StartDate
FROM t_Bar
WHERE Pair=@Pair AND Period = @Priod
)
SELECT Pair,StartDate,COUNT(*) Count, DATENAME(dw,StartDate) Day
FROM D
GROUP BY Pair,StartDate
ORDER BY Pair,StartDate DESC
RETURN


SELECT DATENAME(dw,ES.Date), * 
FROM dbo.fCountByPairPeriod('ESU9',0)ES
FULL JOIN dbo.fCountByPairPeriod('NQU9',0)NQ ON ES.Date= NQ.Date
ORDER BY ISNULL(ES.Date,NQ.date) DESC
RETURN

SELECT DISTINCT Pair
FROM t_Bar
RETURN

DELETE t_Bar
WHERE ID IN(
SELECT         ID
FROM            v_BarBump
WHERE        (Bump > 0.01))
RETURN

DELETE_PAIR:
WHILE 1=1 BEGIN
DELETE TOP (100000) t_Bar
WHERE Pair = @Pair AND Period = @Priod
AND StartDate >= '2019-10-26'
IF @@ROWCOUNT =   0 BREAK
END
RETURN

UPDATE_PAIR:
WHILE 1=1 BEGIN
UPDATE TOP (100000) t_Bar SET Pair = 'ESZ9'
WHERE Pair = @Pair AND Period = @Priod
--AND StartDate > '2017-07-21'
IF @@ROWCOUNT =   0 BREAK
END
RETURN
SELECT * FROM sys.time_zone_info

