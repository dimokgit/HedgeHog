CREATE FUNCTION GetBarsHeight(
  @Top int = 720,
  @Pair varchar(30) = 'EUR/JPY',
  @StartDate datetimeoffset
)RETURNS float AS BEGIN
RETURN(
SELECT 
MAX(PriceAverage)-Min(PriceAverage)Height
FROM
(
SELECT TOP(@Top) (([AskHigh]+[AskLow])/2+([BidHigh]+[BidLow])/2)/2 PriceAverage
  FROM [FOREX].[dbo].[t_Bar]
  WHERE Pair = @Pair AND Period = 1 AND StartDate >= @StartDate
)T
)END