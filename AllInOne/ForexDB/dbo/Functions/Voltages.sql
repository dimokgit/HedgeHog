CREATE FUNCTION [dbo].[Voltages](
@DateStart datetime,
@DateEnd datetime
)RETURNS @Volts TABLE(StartDate datetime,Volts float,Price float)
BEGIN

DECLARE @DateZero datetime

SELECT @DateZero = MAX(StartDate) FROM t_Tick_Volts
WHERE StartDate BETWEEN @DateStart AND @DateEnd

INSERT INTO @Volts
SELECT     T1.StartDate,
SUM(T2.Volts) / (DATEDIFF(ss,T1.StartDate,@DateZero)/60.+1) AS Volts, T1.Price
FROM         t_Tick_Volts AS T1
INNER JOIN t_Tick_Volts AS T2 ON T2.StartDate <= T1.StartDate
WHERE T1.StartDate BETWEEN @DateStart AND @DateEnd
AND T2.StartDate BETWEEN @DateStart AND @DateEnd
GROUP BY T1.StartDate, T1.Price

RETURN
END