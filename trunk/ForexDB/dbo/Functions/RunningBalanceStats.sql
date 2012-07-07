CREATE FUNCTION [dbo].[RunningBalanceStats](
	@SessionId uniqueidentifier
)RETURNS TABLE
RETURN(
WITH A AS
(
SELECT TOP (1) CONVERT(bigint, Id) Id,RunningBalance,TimeOpen,0 Period FROM t_Trade WHERE SessionId = @SessionId
UNION ALL
SELECT CONVERT(bigint, T.Id),T.RunningBalance,T.TimeOpen, CASE WHEN T.RunningBalance < 0 AND A.RunningBalance < 0 THEN A.Period ELSE A.Period+1 END
FROM  t_Trade T INNER JOIN A ON T.Id = A.Id+1
WHERE SessionId = @SessionId
)
, A1 AS
(
SELECT * FROM A WHERE RunningBalance < 0
)
, A2 AS
(
SELECT MIN(RunningBalance)RunningBalance,MAX(TimeOpen)TimeOpen  FROM A1
GROUP BY Period
)
, A3 AS
(
SELECT AVG(RunningBalance)RunningBalance,STDEV(RunningBalance) StDev,AVG(RunningBalance)-STDEV(RunningBalance) RunningBalanceMonthly
FROM A2
)
, A4 AS
(
SELECT TOP 1000000 ROW_NUMBER() OVER(ORDER BY RunningBalance) AS Row,*,(SELECT RunningBalanceMonthly FROM A3) AS RBM FROM A2
)
SELECT AVG(RunningBalance)RunningBalance,STDEV(RunningBalance) StDev,AVG(RunningBalance)-STDEV(RunningBalance) RunningBalanceMonthly FROM A4 WHERE Row > 3
--SELECT * FROM A1
)
--SELECT TOP 4 RBM.*,s.SessionId FROM v_TradeSession s CROSS APPLY RunningBalanceMinimumAverage(s.SessionId) RBM ORDER BY s.TimeStamp DESC
--OPTION (MAXRECURSION  0)
--SELECT * FROM RunningBalanceMinimumAverage('5F7FB6C8-DB47-4FD9-811D-0218B3000A4B')
--OPTION (MAXRECURSION  0)
--SELECT * FROM RunningBalanceMinimumAverage('635A92F4-3089-4960-A823-235741050C56')
--OPTION (MAXRECURSION  0)
--SELECT * FROM RunningBalanceMinimumAverage('CEA510FB-9C1C-4B23-A038-EEE25E749AB7')
--OPTION (MAXRECURSION  0)