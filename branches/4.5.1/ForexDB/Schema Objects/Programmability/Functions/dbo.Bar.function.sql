CREATE FUNCTION [dbo].[Bar](
	@Pair varchar(7),
	@Period tinyint,
	@Date datetime,
	@BarMinutes int
)
RETURNS TABLE
AS
RETURN(
SELECT MAX(AskHigh)AskHigh, MIN(AskLow)AskLow, MAX(BidHigh)BidHigh, MIN(BidLow)BidLow,
MAX(CASE Row WHEN @BarMinutes THEN AskOpen ELSE 0 END)AskOpen,
MAX(CASE Row WHEN 1 THEN AskClose ELSE 0 END)AskClose,
MAX(CASE Row WHEN @BarMinutes THEN BidOpen ELSE 0 END)BidOpen,
MAX(CASE Row WHEN 1 THEN BidClose ELSE 0 END)BidClose,
MAX(CASE Row WHEN @BarMinutes THEN StartDate ELSE '1/1/1900' END)DateOpen,
MAX(CASE Row WHEN 1 THEN DATEADD(n,@Period,StartDate) ELSE '1/1/1900' END)DateClose
FROM BarList(@Pair,@Period,@Date,@BarMinutes)
)
