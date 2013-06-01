CREATE FUNCTION GetBarStats(
  @Pair varchar(30) = 'EUR/JPY',
  @Period int = 1,
  @Length int = 720,
  @StartDate datetimeoffset
)RETURNS TABLE AS
RETURN(
SELECT 
  MIN(StartDate)StartDate 
, MAX(StartDate)StopDate
, MAX(Price_p) - Min(Price_P) BarsHeight
, STDEV(Price_P) PriceStDev
, AVG(PriceHeightInPips) PriceHeight
, SUM(PriceHeightInPips) Distane
, Count(*) Count
FROM
(
SELECT TOP(@Length)  B.Price
  , ABS(B.Price-B1.Price)/B.PipSize PriceHeightInPips
  , B.StartDate
  , B.Price/B.PipSize Price_P
FROM v_Bar B
INNER JOIN v_Bar B1 ON B.Pair = B1.Pair AND B.Period = B1.Period AND B.Row+1 = B1.Row AND B.StartDate < B1.StartDate
WHERE B.Pair = @Pair AND B.Period = 1 AND B.StartDate >= @StartDate
)T
)