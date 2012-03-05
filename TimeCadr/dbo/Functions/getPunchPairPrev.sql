CREATE FUNCTION [dbo].[getPunchPairPrev](
@Start datetimeoffset
)RETURNS TABLE AS
RETURN
(
SELECT        TOP (1) Start, Stop, TotalMinutes
FROM            PunchPair AS PP
WHERE        (Start < @Start)
ORDER BY Start DESC
)