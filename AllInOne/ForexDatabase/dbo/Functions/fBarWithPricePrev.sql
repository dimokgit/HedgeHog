CREATE FUNCTION fBarWithPricePrev(
@Pair varchar(10)
)RETURNS TABLE AS RETURN
WITH T AS
(
SELECT  [Pair]
      ,[Period]
      ,CAST([StartDate] AS date) Date
      ,[StartDate]
      ,([AskHigh]+[BidLow])/2 Price
  FROM [FOREX].[dbo].[t_Bar]
  WHERE Pair=@Pair AND Period = 1
)
SELECT *
FROM T T1
CROSS APPLY (SELECT TOP 1 Price PricePrev FROM T WHERE T.Pair = T1.Pair AND T.Date<T1.Date ORDER BY StartDate DESC) T2