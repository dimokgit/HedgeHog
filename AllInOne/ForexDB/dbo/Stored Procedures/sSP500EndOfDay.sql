CREATE PROCEDURE sSP500EndOfDay AS
WITH T AS
(
SELECT  [Pair]
      ,[Period]
      ,CAST([StartDate] AS date) Date
      ,[StartDate]
      ,([AskHigh]+[BidLow])/2 Price
  FROM [FOREX].[dbo].[t_Bar]B
  INNER JOIN SP500 ON B.Pair = Symbol
  WHERE Period = 1
), T1 AS
(
SELECT DISTINCT T1.Pair,T1.Date,T2.PricePrev
FROM T T1
CROSS APPLY (SELECT TOP 1 Price PricePrev FROM T WHERE T.Pair = T1.Pair AND T.Date = T1.Date ORDER BY StartDate DESC) T2
)
MERGE SP500EndOfDay AS SP
USING T1
ON SP.Pair = T1.Pair AND SP.Date = T1.Date
WHEN NOT MATCHED THEN
INSERT (Pair,Date,Price)
VALUES (T1.Pair,T1.Date,T1.PricePrev)
;
--ORDER BY T1.Pair,T1.Date