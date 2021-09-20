﻿CREATE PROCEDURE [dbo].[sHedgeTrades] AS
WITH T AS
(
SELECT 
T1.[Id]
,T1.TimeOpen
,T1.TimeClose
,T1.[Buy]
,T2.[GrossPL]+T2.GrossPL GrossPL
  FROM [t_Trade] T1
  INNER JOIN t_Trade T2 ON CAST(T1.Id as bigint)=CAST(T2.Id AS bigint)-1  AND T1.Buy=T2.Buy
  WHERE T1.SessionId='80D6FEC2-9448-493D-A07D-CBBDF9CAB6FD' AND T1.Pair='UVXY'
)
SELECT *,
SUM(GrossPL) OVER(ORDER BY ID ROWS UNBOUNDED PRECEDING) AS RunningTotal
FROM T
  