CREATE FUNCTION [dbo].[Corridor](
	@Pair varchar(7),
	@Period tinyint,
	@Date datetime,
	@SpreadPeriod int
)
RETURNS real
AS
BEGIN
RETURN(
SELECT (Max(High)-Min(Low))/(COUNT(*)/(60*24/@Period)) FROM
(
SELECT     TOP (@SpreadPeriod) 
(AskHigh + BidHigh ) / 2 AS High,
(AskLow + BidLow) / 2 AS Low
FROM         dbo.t_Bar
WHERE Pair = @Pair AND Period = @Period AND (StartDate <= @Date)
ORDER BY StartDate DESC
)T 
)
END