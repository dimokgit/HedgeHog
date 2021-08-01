CREATE FUNCTION [dbo].[RunningBalanceMinimumAverage](
	@SessionId uniqueidentifier
)RETURNS TABLE
RETURN(
WITH A AS
(
SELECT TOP (1) CONVERT(bigint, Id) Id,RunningBalance,Lot,TimeOpen,0 Period FROM t_Trade WHERE SessionId = @SessionId
UNION ALL
SELECT CONVERT(bigint, T.Id),T.RunningBalance,T.Lot,T.TimeOpen, CASE WHEN T.RunningBalance < 0 AND A.RunningBalance < 0 THEN A.Period ELSE A.Period+1 END
FROM  t_Trade T INNER JOIN A ON T.Id = A.Id+1
WHERE SessionId = @SessionId
)
, A1 AS
(
SELECT * FROM A WHERE RunningBalance < 0
)
SELECT ROUND(MIN(RunningBalance),0)RunningBalance,MAX(Lot) Lot,MAX(TimeOpen)TimeOpen  FROM A1
GROUP BY Period
)
