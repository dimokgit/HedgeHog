﻿CREATE VIEW v_BarTest AS
SELECT  Pair, Period, 
(SELECT MIN([Min])FROM(
SELECT 
MIN(B.AskHigh) AS [Min]
UNION ALL
SELECT MIN(AskLow)
UNION ALL
SELECT MIN(AskOpen)
UNION ALL
SELECT MIN(AskClose)
UNION ALL
SELECT MIN(BidHigh)
UNION ALL
SELECT MIN(BidLow)
UNION ALL
SELECT MIN(BidOpen)
UNION ALL
SELECT MIN(BidClose))T)Minimun,
(SELECT MAX([Max])FROM(
SELECT 
MAX(B.AskHigh) AS [Max]
UNION ALL
SELECT MAX(AskLow)
UNION ALL
SELECT MAX(AskOpen)
UNION ALL
SELECT MAX(AskClose)
UNION ALL
SELECT MAX(BidHigh)
UNION ALL
SELECT MAX(BidLow)
UNION ALL
SELECT MAX(BidOpen)
UNION ALL
SELECT MAX(BidClose))T)Maximum

FROM         t_Bar B
GROUP BY Pair, Period