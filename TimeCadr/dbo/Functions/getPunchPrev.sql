CREATE FUNCTION [dbo].[getPunchPrev](
@Time datetimeoffset
)RETURNS TABLE AS
RETURN
(
SELECT        TOP (1) Id, Time, DirectionId, TypeId, IsOutOfSequence, TimeUTC, TimeZoneOffset
FROM            Punch
WHERE        (Time < @Time)
ORDER BY Time DESC
)