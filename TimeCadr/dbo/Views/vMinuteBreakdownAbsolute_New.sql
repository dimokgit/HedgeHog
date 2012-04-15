
CREATE VIEW [dbo].[vMinuteBreakdownAbsolute_New] AS
SELECT
 BASE.WorkShiftStart,BASE.PunchPairStart
,BASE.MinuteDate,BASE.MinuteDateTime
,RateCodeId,RuleId,RateCodeLayerPriority,RateCodeTypePriority,IsRuleOver,IsRuleExtra
,RCBR.HourStart * 60 AS GracePeriod
FROM   dbo.vWorkShiftMinuteBreakdown_10 AS BASE
INNER JOIN vRateCodeByRange AS RCBR ON 
  CASE RCBR.IsTimeBetween
    WHEN 1 THEN (CASE WHEN BASE.MinuteTime >= RCBR.TimeStart_ AND BASE.MinuteTime < RCBR.TimeStop THEN 1 ELSE 0 END)
    ELSE (CASE WHEN BASE.MinuteTime < RCBR.TimeStop OR BASE.MinuteTime >= RCBR.TimeStart_  THEN 1 ELSE 0 END)
  END = 1
WHERE RCBR.IsTimeAbsolute = 1