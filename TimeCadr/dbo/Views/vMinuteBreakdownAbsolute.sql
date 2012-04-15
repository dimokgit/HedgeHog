CREATE VIEW [dbo].[vMinuteBreakdownAbsolute] AS
SELECT
 BASE.WorkShiftStart,BASE.PunchPairStart
,BASE.MinuteDate,BASE.MinuteDateTime
,RateCodeId,RuleId,RateCodeLayerPriority,RateCodeTypePriority,IsRuleOver,IsRuleExtra
--,DATEPART(hh,BASE.WorkShiftStart)Hour,RCBR.TimeStart
--,DATEADD(hh,RCBR.TimeStart, dbo.GetDate(BASE.WorkShiftStart))RateStart
--,DATEADD(hh,RCBR.TimeStart+ RCBR.HourStop, dbo.GetDate(BASE.WorkShiftStart))RateStop
FROM   dbo.vWorkShiftMinuteBreakdown_10 AS BASE
INNER JOIN vRateCodeByRange AS RCBR ON 
  RCBR.RateCodeLayerId = 1 
  --AND DATEPART(hh,BASE.WorkShiftStart) < RCBR.TimeStart 
  AND CAST(BASE.MinuteDateTime AS datetime)>= DATEADD(hh,RCBR.TimeStart, dbo.GetDate(BASE.WorkShiftStart))
  AND CAST(BASE.MinuteDateTime AS datetime)< DATEADD(hh,RCBR.TimeStart+ RCBR.HourStop, dbo.GetDate(BASE.WorkShiftStart))
WHERE RCBR.IsTimeAbsolute = 1
--ORDER BY BASE.WorkShiftStart,BASE.MinuteDateTime