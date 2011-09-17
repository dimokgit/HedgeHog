CREATE FUNCTION [dbo].[BarList](
	@Pair varchar(7),
	@Period tinyint,
	@Date datetime,
	@BarMinutes int
)
RETURNS TABLE
AS
RETURN(
SELECT     TOP (@BarMinutes) ROW_NUMBER()OVER(ORDER BY StartDate DESC) AS Row,
 StartDate,AskHigh, AskLow, AskOpen, AskClose, BidHigh, BidLow, BidOpen, BidClose
FROM         t_Bar
WHERE     (Pair = @Pair) AND (Period = @Period) AND (StartDate <= @Date)
ORDER BY StartDate DESC
)
