CREATE PROCEDURE [dbo].[s_Bar_Fix] AS
DECLARE @Pair sysname,@Period int,@StartDate datetime
SELECT @Pair = 'EUR/USD' ,@Period = 60,@StartDate = '2007-04-06 15:00:00.000'
SELECT * FROM t_Bar
WHERE Pair = @Pair
AND Period = @Period
AND StartDate = @StartDate

RETURN
UPDATE t_Bar SET AskLow = 1.3370,AskClose = 1.33745,BidLow = 1.3369,BidClose = 1.33735
WHERE Pair = @Pair
AND Period = @Period
AND StartDate = @StartDate