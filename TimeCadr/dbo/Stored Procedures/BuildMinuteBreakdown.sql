--SELECT RC.RateCodeLayerPriority,RC.RateCodeTypePriority FROM vRateCodeByRange RC ORDER BY RC.RateCodeLayerPriority,RC.RateCodeTypePriority
CREATE PROCEDURE BuildMinuteBreakdown AS 
SET NOCOUNT ON
SET FMTONLY OFF
DECLARE @Proprity int,@LayerPriority int SELECT @Proprity = -1,@LayerPriority = -1

SELECT * INTO #Breakdown_10
FROM GetMinuteBreakdownByLayerAndType(-1,-1)

WHILE 0 = 0 BEGIN ---------------> Loop Start

SELECT TOP 1 @LayerPriority = RC.RateCodeLayerPriority,@Proprity = RC.RateCodeTypePriority FROM vRateCode RC 
WHERE RC.RateCodeLayerPriority*10 +RC.RateCodeTypePriority > @LayerPriority * 10 + @Proprity
ORDER BY RC.RateCodeLayerPriority,RC.RateCodeTypePriority

IF @@ROWCOUNT = 0 BREAK

INSERT INTO #Breakdown_10
SELECT * FROM GetMinuteBreakdownByLayerAndType(@LayerPriority,@Proprity)
WHERE NOT MinuteDateTime IN
(SELECT BD.MinuteDateTime FROM #Breakdown_10 BD WHERE BD.RateCodeTypePriority >= @Proprity)

DELETE FROM BD
FROM #Breakdown_10 BD INNER JOIN
(
SELECT BD.MinuteDateTime,BD.RateCodeTypePriority FROM #Breakdown_10 BD
EXCEPT
SELECT BD.MinuteDateTime,MAX(BD.RateCodeTypePriority)TypePriorityMax FROM #Breakdown_10 BD GROUP BY BD.MinuteDateTime
)BDD ON BD.MinuteDateTime = BDD.MinuteDateTime AND BD.RateCodeTypePriority = BDD.RateCodeTypePriority

END ---------------> Loop End

SELECT ROW_NUMBER() OVER(PARTITION BY WorkShiftStart,RateCodeID ORDER BY MinuteDateTime) WSRow, * 
INTO #Breakdown_20
FROM #Breakdown_10 ORDER BY MinuteDateTime

--SELECT * FROM #Breakdown_20 BD

DECLARE @Out TABLE( WorkShiftStart datetimeoffset NOT NULL
                  ,	MinuteDate datetime NOT NULL
                  ,	Minutes int NOT NULL
                  ,	Hour int NOT NULL
                  ,	Minute int NOT NULL
                  ,	RateCode varchar(50) NOT NULL
                  ,	RateCodeId int NOT NULL
                  ,	Layer varchar(50) NOT NULL
                  ,	Type varchar(50) NOT NULL
                  )

INSERT INTO @Out
SELECT BD.WorkShiftStart,BD.MinuteDate,COUNT(*)Minutes,dbo.CalcHour(COUNT(*))Hour,dbo.CalcHourMinute(COUNT(*))Minute,RC.Name RateCode,BD.RateCodeId,RC.Layer,RC.Type
FROM #Breakdown_20 BD
INNER JOIN vRateCode RC ON BD.RateCodeId = RC.Id
GROUP BY BD.RateCodeId, BD.MinuteDate, BD.WorkShiftStart,BD.RateCodeId,RC.Name,RC.Layer,RC.Type,RC.RateCodeLayerPriority,RC.RateCodeTypePriority
ORDER BY BD.WorkShiftStart, BD.MinuteDate,RC.RateCodeLayerPriority,RC.RateCodeTypePriority

SELECT WorkShiftStart
                  ,	MinuteDate
                  ,	Minutes
                  ,	Hour
                  ,	Minute
                  ,	RateCode
                  ,	RateCodeId
                  ,	Layer
                  ,	Type
FROM @Out
ORDER BY WorkShiftStart, MinuteDate, RateCodeId