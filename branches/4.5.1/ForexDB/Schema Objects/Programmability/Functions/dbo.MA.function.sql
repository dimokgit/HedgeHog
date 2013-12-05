CREATE FUNCTION MA(
	@Pair varchar(7),
	@Period tinyint,
	@Date datetime,
	@MAPeriod tinyint
)
RETURNS real
AS
BEGIN
RETURN(
SELECT AVG(Spread) FROM
(
SELECT     TOP (@MAPeriod) (AskHigh - AskLow + BidHigh - BidLow) / 2 AS Spread
FROM         dbo.t_Bar
WHERE Pair = @Pair AND Period = @Period AND (StartDate <= @Date)
ORDER BY StartDate DESC
)T)
END
