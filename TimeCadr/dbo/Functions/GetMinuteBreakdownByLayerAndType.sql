CREATE FUNCTION GetMinuteBreakdownByLayerAndType(
  @LayerPriority int,
  @TypePriority int
)RETURNS TABLE AS
RETURN(
SELECT        BASE.WorkShiftStart, BASE.PunchPairStart, BASE.MinuteDate, BASE.MinuteDateTime, BASE.WSMinute, BASE.WSHour, BASE.WSHourMinute, BASE.WDMinute, 
                         BASE.WDHour, BASE.WDHourMinute, RCBR.RateCodeLayerPriority, RCBR.RateCodeTypePriority, RCBR.RateCodeId, RCBR.RateCode
FROM            vMinuteBreakdown_00 AS BASE INNER JOIN
                         vRateCodeByRange AS RCBR ON 
                         RCBR.RateCodeLayerId = 1 AND BASE.WSHour >= RCBR.HourStart AND BASE.WSHour <= RCBR.HourStop OR 
                         RCBR.RateCodeLayerId = 2 AND BASE.WDHour >= RCBR.HourStart AND BASE.WDHour <= RCBR.HourStop
WHERE        (RCBR.RateCodeLayerPriority = @LayerPriority) AND (RCBR.RateCodeTypePriority = @TypePriority)
)