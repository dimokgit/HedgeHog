CREATE VIEW [dbo].[vConsensus] AS
WITH T AS
(
SELECT  [Pair]
      ,[Period]
      ,CAST([StartDate] AS date) Date
      ,[StartDate]
      ,([AskHigh]+[BidLow])/2 Price
  FROM [t_Bar]B
  INNER JOIN SP500 ON B.Pair = Symbol
  WHERE Period = 1 AND SP500.IsConsensus = 1
), T1 AS
(
SELECT T.Pair,T.StartDate,(T.Price-T2.Price)/T2.Price*100 PricePerc
FROM T
CROSS APPLY (SELECT TOP 1 Price FROM SP500EndOfDay SP WHERE T.Pair = SP.Pair AND T.Date>SP.Date ORDER BY SP.Date DESC) T2
),T2 AS
(
SELECT *
,PricePerc- MIN(PricePerc) OVER(PARTITION BY StartDate) PricePercRel
FROM T1 T
)
SELECT T.StartDate,STDEV(PricePercRel)/AVG(PricePercRel)*100PricePercRSD
FROM T2 T
GROUP BY T.StartDate
--ORDER BY T.StartDate--,T.Pair
