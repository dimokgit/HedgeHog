CREATE PROCEDURE [dbo].[sVolatility] AS
DECLARE @Start datetimeoffset,@Stop datetimeoffset
SELECT @Stop = '2/5/2014',@Start = DATEADD(dd,-4,@Stop)
--SELECT @Stop ,@Start 

;WITH T0 AS
(
SELECT  
       ([AskOpen]+[BidOpen])/2 [Open]
      ,([AskClose]+[BidClose])/2 [Close]
      ,[StartDate]
      ,DATEPART(DW,StartDate) DW
      ,DATEPART(HH,StartDate) HH
  FROM [t_Bar]
  WHERE Period = 1
    AND Pair = 'USD/JPY'
    AND StartDate BETWEEN @Start AND @Stop
), T1 AS
(
SELECT T.*,A.Average
FROM T0 T
CROSS APPLY
(
SELECT TOP 60 AVG(ABS([Open]-[Close])) Average
FROM T0 WHERE T0.StartDate>=T.StartDate
)A
)
SELECT MIN(StartDate)StartDate, DW,HH,AVG(Average)Average 
FROM T1
GROUP BY DW,HH
ORDER BY DW,HH
