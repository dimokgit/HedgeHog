CREATE VIEW vBarSP500 AS
SELECT B.StartDate,(B.AskHigh+B.BidLow)/2 Price, C.PricePercRSD
FROM t_Bar B
INNER JOIN [dbo].[vConsensus] C ON B.Pair='SPY' AND B.Period = 1 AND C.StartDate = B.StartDate
--ORDER BY B.StartDate