--- Test
--SELECT SUM(RC.Rate) Rate FROM @Out O INNER JOIN RateCode RC ON O.RateCodeId = RC.Id
--[BuildMinuteBreakdown]
CREATE PROCEDURE [dbo].[BuildMinuteBreakdown]
AS 
SET NOCOUNT ON
SET FMTONLY OFF
DECLARE @TypePriority int,@LayerPriority int SELECT @TypePriority = -1,@LayerPriority = -1

SELECT * INTO #Breakdown_00
FROM vMinuteBreakdown

SELECT TOP (0) * INTO #Breakdown_10
FROM #Breakdown_00

WHILE 0 = 0 BEGIN ---------------> OVER Loop

SELECT TOP 1 @LayerPriority = RC.RateCodeLayerPriority,@TypePriority = RC.RateCodeTypePriority FROM vRateCode RC 
WHERE RC.IsRuleOver = 1 AND RC.RateCodeLayerPriority*10 +RC.RateCodeTypePriority > @LayerPriority * 10 + @TypePriority
ORDER BY RC.RateCodeLayerPriority,RC.RateCodeTypePriority

IF @@ROWCOUNT = 0 BREAK

INSERT INTO #Breakdown_10
SELECT * FROM #Breakdown_00 B
WHERE B.IsRuleOver = 1 AND B.RateCodeLayerPriority = @LayerPriority AND B.RateCodeTypePriority = @TypePriority
  AND NOT MinuteDateTime IN (SELECT BD.MinuteDateTime FROM #Breakdown_10 BD WHERE BD.RateCodeTypePriority >= @TypePriority)

DELETE FROM BD
FROM #Breakdown_10 BD INNER JOIN
(
SELECT BD.MinuteDateTime,BD.RateCodeTypePriority FROM #Breakdown_10 BD
EXCEPT
SELECT BD.MinuteDateTime,MAX(BD.RateCodeTypePriority)TypePriorityMax FROM #Breakdown_10 BD GROUP BY BD.MinuteDateTime
)BDD ON BD.MinuteDateTime = BDD.MinuteDateTime AND BD.RateCodeTypePriority = BDD.RateCodeTypePriority

END ---------------> OVER Loop

SELECT @TypePriority = -1,@LayerPriority = -1
SELECT TOP (0) * INTO #Breakdown_20
FROM #Breakdown_00

WHILE 0 = 0 BEGIN ---------------> EXTRA Loop

SELECT TOP 1 @LayerPriority = RC.RateCodeLayerPriority,@TypePriority = RC.RateCodeTypePriority FROM vRateCode RC 
WHERE RC.IsRuleExtra = 1 AND RC.RateCodeLayerPriority*10 +RC.RateCodeTypePriority > @LayerPriority * 10 + @TypePriority
ORDER BY RC.RateCodeLayerPriority,RC.RateCodeTypePriority

IF @@ROWCOUNT = 0 BREAK

INSERT INTO #Breakdown_20
SELECT * FROM #Breakdown_00 B
WHERE B.IsRuleExtra = 1 AND B.RateCodeLayerPriority = @LayerPriority AND B.RateCodeTypePriority = @TypePriority


DELETE FROM BD
FROM #Breakdown_20 BD INNER JOIN
(
SELECT BD.MinuteDateTime,BD.RateCodeTypePriority FROM #Breakdown_20 BD
EXCEPT
SELECT BD.MinuteDateTime,MAX(BD.RateCodeTypePriority)TypePriorityMax FROM #Breakdown_20 BD GROUP BY BD.MinuteDateTime
)BDD ON BD.MinuteDateTime = BDD.MinuteDateTime AND BD.RateCodeTypePriority = BDD.RateCodeTypePriority

END ---------------> EXTRA Loop


SELECT ROW_NUMBER() OVER(PARTITION BY WorkShiftStart,RateCodeID ORDER BY MinuteDateTime) WSRow, * 
INTO #Breakdown
FROM
(
SELECT * FROM #Breakdown_10
UNION ALL
SELECT * FROM #Breakdown_20
)U
ORDER BY MinuteDateTime,RuleId

--SELECT * FROM #Breakdown BD

DECLARE @Out TABLE( WorkShiftStart datetimeoffset NOT NULL
                  ,	MinuteDate datetime NOT NULL
                  , Start datetimeoffset NOT NULL
                  , [End] datetimeoffset NOT NULL
                  ,	Minutes int NOT NULL
                  ,	Hour int NOT NULL
                  ,	Minute int NOT NULL
                  ,	RateCode varchar(50) NOT NULL
                  ,	RateCodeId int NOT NULL
                  ,	Layer varchar(50) NOT NULL
                  ,	Type varchar(50) NOT NULL
                  , RateCodeTypePriority int NOT NULL
                  , RateCodeLayerPriority int NOT NULL
                  , RulePriority int NOT NULL
                  , [Rule] varchar(1) NOT NULL
                  )

INSERT INTO @Out
SELECT BD.WorkShiftStart
, BD.MinuteDate, MIN(BD.MinuteDateTime)Start, MAX(BD.MinuteDateTime)[End]
, COUNT(*)Minutes, dbo.CalcHour(COUNT(*))Hour,dbo.CalcHourMinute(COUNT(*))Minute
, RC.Name RateCode,BD.RateCodeId,RC.Layer,RC.Type,RC.RateCodeTypePriority,RC.RateCodeLayerPriority
, RC.RulePriority, LEFT(RC.[Rule],1)[Rule]
FROM #Breakdown BD
INNER JOIN vRateCode RC ON BD.RateCodeId = RC.Id
GROUP BY BD.RateCodeId, BD.MinuteDate, BD.PunchPairStart, BD.WorkShiftStart,BD.RateCodeId,RC.Name,RC.Layer,RC.Type,RC.RateCodeLayerPriority,RC.RateCodeTypePriority,RC.RulePriority,RC.[Rule]
ORDER BY BD.WorkShiftStart, BD.PunchPairStart, BD.MinuteDate,RC.RateCodeLayerPriority,RC.RateCodeTypePriority,RC.RulePriority

SELECT  WorkShiftStart
      ,	MinuteDate
      , Start
      , [End]
      ,	Minutes
      ,	Hour
      ,	Minute
      ,	RateCode
      ,	RateCodeId
      ,	Layer
      ,	Type
      , [Rule]
FROM @Out
ORDER BY WorkShiftStart,Start, RateCodeTypePriority,RateCodeLayerPriority,RulePriority


TRUNCATE TABLE SalaryBreakdown
INSERT INTO SalaryBreakdown(RateCode,Salary)
SELECT CASE GROUPING(RateCode) WHEN 1 THEN 'Total' ELSE RateCode END RateCode,SUM(Salary)Salary
FROM
(
SELECT ROW_NUMBER() OVER(ORDER BY (SELECT 1)) Row, MIN(O.RateCode)RateCode, CONVERT(numeric(10,2),MIN(RC.Rate)*SUM(O.Minutes)/60.0) Salary
FROM RateCode RC
INNER JOIN @Out O ON O.RateCodeId = RC.Id
GROUP BY RC.Id
)G
GROUP BY Row,RateCode WITH ROLLUP
HAVING GROUPING(Row)+GROUPING(RateCode) IN(0,2)
ORDER BY Row