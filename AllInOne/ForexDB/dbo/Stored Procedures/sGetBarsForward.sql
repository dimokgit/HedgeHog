CREATE PROCEDURE [dbo].[sGetBarsForward]
@Pair varchar(10),
@Period int,
@DateMax datetimeoffset,
@Count int
AS

SELECT        TOP (@Count) 
Pair
, Period
, SWITCHOFFSET(Startdate, '+00:00')StartDate
, AskHigh
, AskLow
, AskOpen
, AskClose
, BidHigh
, BidLow
, BidOpen
, BidClose
, Volume
, ID
, StartDateLocal
, Row
FROM            t_Bar
WHERE Pair = @pair AND Period = @Period AND StartDate >= @DateMax


