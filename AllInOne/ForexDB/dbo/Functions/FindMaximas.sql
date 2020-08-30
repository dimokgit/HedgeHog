CREATE FUNCTION [dbo].[FindMaximas](
--@DateStart datetime,
--@DateEnd datetime,
@TimeFrame int = 60,
@Volts AS dt_VoltsTable READONLY
)RETURNS TABLE AS
RETURN(
SELECT V1.StartDate, V1.Volts, V1.Price
FROM @Volts AS V1 
INNER JOIN @Volts AS V2 
	ON V2.StartDate BETWEEN DATEADD(n, - @TimeFrame, V1.StartDate) AND DATEADD(n, @TimeFrame, V1.StartDate)
--WHERE V1.StartDate BETWEEN @DateStart AND @DateEnd
GROUP BY V1.StartDate, V1.Volts,V1.Price
HAVING      (MAX(V2.Volts) = V1.Volts)
)
--SELECT @DateStart = '7/24/08 7:54', @DateEnd = DATEADD(yy,20,@DateStart)
