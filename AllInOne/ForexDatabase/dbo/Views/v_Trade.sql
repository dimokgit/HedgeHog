


CREATE VIEW [dbo].[v_Trade]AS
WITH T AS
(
select ROW_NUMBER() OVER(PARTITION BY T.Pair,T.SessionID ORDER BY Id) Row, *
from t_Trade T
), T1 AS
(
SELECT Row,
SUM(CAST(Buy as int)) Buy, SUM(T.PL) PL, SUM(T.GrossPL) AS GrossPL
, SUM(T.Lot) Lot, MAX(T.TimeOpen)TimeOpen, MAX(T.TimeClose)TimeClose, SUM(T.Commission) Commission
, MAX(T.TimeStamp)TimeStamp
, T.SessionId
,MAX(T.Id) Id
FROM T
GROUP BY T.SessionId,T.Row
),T2 AS
(
SELECT T.*
, T0.Pair
, T0.RunningBalance, T0.RunningBalanceTotal
FROM T1 T
INNER JOIN t_Trade T0 ON T.Id=T0.Id
)
SELECT        
T.Id, T.Buy, T.PL, T.GrossPL - T.Commission * T.Lot * 0 / 10000.0 AS GrossPL
, T.Lot, T.Pair, T.TimeOpen, T.TimeClose, T.Commission
, T.TimeStamp
, T.SessionId
, DATEDIFF(hh, T.TimeOpen, T.TimeClose) AS TradeLength
, CONVERT(bit, CASE WHEN T .pl > 0 THEN 1 ELSE 0 END) AS HasProfit
, DATEDIFF(n, T.TimeOpen, T.TimeClose) AS TradeLengthInMinutes
, cast(T.TimeOpen as date) AS DateOpen
, CAST(T.TimeClose AS date) AS DateClose
, DATEPART(dw, T.TimeOpen) AS DW
, dbo.ISOweek(T.TimeOpen) AS WM
, T.RunningBalance, T.RunningBalanceTotal
, S.SuperSessionUid
, TV.Angle, TV.Minutes, TV.Voltage, TV.Voltage2, TV.CmaPasses, TV.StDev
, TV.PPM, TV.PpmM1, TV.M1Angle, TV.StDevAvg
FROM            T2 AS T INNER JOIN
                         dbo.v_TradeSession AS S ON T.SessionId = S.SessionId AND T.Pair = S.Pair
						 LEFT OUTER JOIN
                         dbo.v_TradeValue AS TV ON T.Id = TV.TradeId
