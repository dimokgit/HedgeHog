CREATE VIEW dbo.v_BarStats
AS
SELECT     Pair, Year, Week, STDEV(PriceStDev) AS StDev, AVG(PriceStDev) AS Average
FROM         (SELECT     Pair, YEAR(StartDate) AS Year, MONTH(StartDate) AS Month, DATEPART(wk, StartDate) AS Week, ((AskHigh + AskLow) / 2 + (BidHigh + BidLow) / 2) 
                                              / 2 AS PriceStDev
                       FROM          dbo.t_Bar
                       WHERE      (Period = 15)) AS T
GROUP BY Pair, Year, Week