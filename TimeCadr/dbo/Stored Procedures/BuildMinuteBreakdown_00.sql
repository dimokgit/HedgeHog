--SELECT RC.RateCodeLayerPriority,RC.RateCodeTypePriority FROM vRateCodeByRange RC ORDER BY RC.RateCodeLayerPriority,RC.RateCodeTypePriority
CREATE PROCEDURE [BuildMinuteBreakdown_00] AS SET NOCOUNT ON
DECLARE @Proprity int

SELECT * INTO #BreakDdown
FROM GetMinuteBreakdownByLayerAndType(0,0)

SET @Proprity = 1
INSERT INTO #BreakDdown
SELECT * FROM GetMinuteBreakdownByLayerAndType(1,@Proprity)
WHERE NOT MinuteDateTime IN
(SELECT BD.MinuteDateTime FROM #BreakDdown BD WHERE BD.RateCodeTypePriority >= @Proprity)

SET @Proprity = 2
INSERT INTO #BreakDdown
SELECT * FROM GetMinuteBreakdownByLayerAndType(1,@Proprity)
WHERE NOT MinuteDateTime IN
(SELECT BD.MinuteDateTime FROM #BreakDdown BD WHERE BD.RateCodeTypePriority >= @Proprity)

DELETE FROM BD
FROM #BreakDdown BD INNER JOIN
(
SELECT BD.MinuteDateTime,BD.RateCodeTypePriority FROM #BreakDdown BD
EXCEPT
SELECT BD.MinuteDateTime,MAX(BD.RateCodeTypePriority)TypePriorityMax FROM #BreakDdown BD GROUP BY BD.MinuteDateTime
)BDD ON BD.MinuteDateTime = BDD.MinuteDateTime AND BD.RateCodeTypePriority = BDD.RateCodeTypePriority

SET @Proprity = 3
INSERT INTO #BreakDdown
SELECT * FROM GetMinuteBreakdownByLayerAndType(1,@Proprity)

DELETE FROM BD
FROM #BreakDdown BD INNER JOIN
(
SELECT BD.MinuteDateTime,BD.RateCodeTypePriority FROM #BreakDdown BD
EXCEPT
SELECT BD.MinuteDateTime,MAX(BD.RateCodeTypePriority)TypePriorityMax FROM #BreakDdown BD GROUP BY BD.MinuteDateTime
)BDD ON BD.MinuteDateTime = BDD.MinuteDateTime AND BD.RateCodeTypePriority = BDD.RateCodeTypePriority


SET @Proprity = 2
INSERT INTO #BreakDdown
SELECT * FROM GetMinuteBreakdownByLayerAndType(2,@Proprity)
WHERE NOT MinuteDateTime IN
(SELECT BD.MinuteDateTime FROM #BreakDdown BD WHERE BD.RateCodeTypePriority >= @Proprity)

DELETE FROM BD
FROM #BreakDdown BD INNER JOIN
(
SELECT BD.MinuteDateTime,BD.RateCodeTypePriority FROM #BreakDdown BD
EXCEPT
SELECT BD.MinuteDateTime,MAX(BD.RateCodeTypePriority)TypePriorityMax FROM #BreakDdown BD GROUP BY BD.MinuteDateTime
)BDD ON BD.MinuteDateTime = BDD.MinuteDateTime AND BD.RateCodeTypePriority = BDD.RateCodeTypePriority

--SELECT * FROM #BreakDdown RETURN

SET @Proprity = 3
INSERT INTO #BreakDdown
SELECT * FROM GetMinuteBreakdownByLayerAndType(2,@Proprity)
WHERE NOT MinuteDateTime IN
(SELECT BD.MinuteDateTime FROM #BreakDdown BD WHERE BD.RateCodeTypePriority >= @Proprity)

DELETE FROM BD
FROM #BreakDdown BD INNER JOIN
(
SELECT BD.MinuteDateTime,BD.RateCodeTypePriority FROM #BreakDdown BD
EXCEPT
SELECT BD.MinuteDateTime,MAX(BD.RateCodeTypePriority)TypePriorityMax FROM #BreakDdown BD GROUP BY BD.MinuteDateTime
)BDD ON BD.MinuteDateTime = BDD.MinuteDateTime AND BD.RateCodeTypePriority = BDD.RateCodeTypePriority

SELECT ROW_NUMBER() OVER(PARTITION BY WorkShiftStart,RateCodeID ORDER BY MinuteDateTime) WSRow, * 
INTO #Breakdown_20
FROM #BreakDdown ORDER BY MinuteDateTime

SELECT * FROM #Breakdown_20 BD

SELECT BD.WorkShiftStart,COUNT(*)Minutes,dbo.CalcHour(COUNT(*))Hour,dbo.CalcHourMinute(COUNT(*))Minute,BD.RateCodeId FROM #Breakdown_20 BD
GROUP BY BD.WorkShiftStart, BD.RateCodeId
ORDER BY BD.WorkShiftStart, BD.RateCodeId

DROP TABLE #BreakDdown