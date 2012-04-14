CREATE FUNCTION [dbo].[GetMinuteBreakdownByLayerAndType](
  @LayerPriority int,
  @TypePriority int
)RETURNS TABLE AS
RETURN(
SELECT        BASE.WorkShiftStart, BASE.PunchPairStart, BASE.MinuteDate, BASE.MinuteDateTime, BASE.WSMinute, BASE.WSHour, BASE.WSHourMinute, BASE.WDMinute, 
                         BASE.WDHour, BASE.WDHourMinute, BASE.RateCodeLayerPriority, BASE.RateCodeTypePriority, BASE.RateCodeId, BASE.RateCode
FROM            vMinuteBreakdown AS BASE
WHERE        (BASE.RateCodeLayerPriority = @LayerPriority) AND (BASE.RateCodeTypePriority = @TypePriority)
)