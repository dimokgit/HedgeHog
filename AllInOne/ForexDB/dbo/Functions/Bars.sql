CREATE FUNCTION dbo.Bars(
@Pair varchar(7),
@Period tinyint
)RETURNS @T TABLE (Date	datetime,Ticks	int,AskAvg	float,AskHigh	float,AskLow	float,BidAvg	float,BidHigh	float,BidLow	float)
AS BEGIN

INSERT INTO @T
SELECT     dbo.RoundToMinute(StartDate,  @Period) AS Date, COUNT(*) AS Ticks,
AVG(Ask) AS AskAvg, MAX(Ask) AS AskHigh, MIN(Ask) AS AskLow,
AVG(Bid)  AS BidAvg, MAX(Bid) AS BidHigh, MIN(Bid) AS BidLow
FROM         t_Tick
WHERE Pair = @Pair
GROUP BY dbo.RoundToMinute(StartDate, @Period)


RETURN
END
