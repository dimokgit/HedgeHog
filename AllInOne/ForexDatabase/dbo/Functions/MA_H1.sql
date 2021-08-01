CREATE FUNCTION [dbo].[MA_H1](
	@Pair varchar(7),
	@Period tinyint,
	@Date datetime,
	@MAPeriod tinyint
)
RETURNS real
AS
BEGIN
DECLARE @Spread real SET @Spread = dbo.MA(@Pair,@Period,@Date,@MAPeriod)
RETURN(
SELECT AVG(Spread) FROM
(
SELECT     TOP (@MAPeriod) (AskHigh - AskLow + BidHigh - BidLow) / 2 AS Spread
FROM         dbo.t_Bar
WHERE Pair = @Pair AND Period = @Period AND (StartDate <= @Date) AND (AskHigh - AskLow + BidHigh - BidLow) / 2 >= @Spread
ORDER BY StartDate DESC
)T )
END