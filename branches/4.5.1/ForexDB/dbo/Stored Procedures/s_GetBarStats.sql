CREATE PROCEDURE [dbo].[s_GetBarStats] 
  @Pair varchar(30) = 'EUR/USD',
  @Period int = 1,
  @Length int = 720,
  @StartDate datetime = '1/1/2011'
AS
DECLARE @D float SET @D = 0.5
SELECT * FROM(
SELECT
StopDateMonth, StopDateHour
, BarsHeightAvg
, BarsHeightStd
, (BarsHeightAvg - BarsHeightStd * @D) BarsHeight_N
, STDEV((BarsHeightAvg - BarsHeightStd * @D)) OVER(PARTITION BY 1) BarsHeight_NStd
--,	PriceHeight
--,	PriceStDev	
,	Distance
,	DistToHeightAvg
,	DistToHeightStd
, DistToHeightStd/DistToHeightAvg DistToHeightVol
,	DistToHeightMax
,	DistToHeightMin
,	HeightToStDevAvg
,	HeightToStDevStd
,	HeightToStDevAvg - HeightToStDevStd HeightToStDev_N
,	STDEV(HeightToStDevAvg - HeightToStDevStd)OVER(PARTITION BY 1) HeightToStDev_NStd
, HeightToStDevStd/HeightToStDevAvg HeightToStDevVol
FROM
(
SELECT 
  StopDateMonth, StopDateHour
, AVG(BarsHeight)BarsHeightAvg
, STDEV(BarsHeight)BarsHeightStd
,	AVG(PriceHeight)PriceHeight
,	AVG(PriceStDev)PriceStDev
,	AVG(Distance)Distance
, AVG(DistToHeight) DistToHeightAvg
, MAX(DistToHeight) DistToHeightMax
, MIN(DistToHeight) DistToHeightMin
, STDEV(DistToHeight) DistToHeightStd
, AVG(BarsHeightToPriceStDev) HeightToStDevAvg
, STDEV(BarsHeightToPriceStDev) HeightToStDevStd
FROM
(
SELECT
  Pair
, BarsHeight
,	PriceHeight
,	PriceStDev	
, BarsHeight/PriceStDev BarsHeightToPriceStDev
,	Distance
,	DATEPART(hh,StopDateLocal) StopDateHour
--, dbo.Date( DATEADD(dd,7 - DATEPART(dw,StopDateLocal),StopDateLocal)) StopDateMonth
, CAST(StopDateLocal AS date) StopDateMonth
, Distance/BarsHeight DistToHeight
FROM t_BarStats NOLOCK
WHERE Pair = @Pair AND Period = @Period AND Length = @Length AND StartDateLocal >= @StartDate
)T GROUP BY StopDateMonth, StopDateHour
)T
)T
ORDER BY 
--StopDateHour,StopDateMonth
StopDateMonth,StopDateHour
--EXEC s_GetBarStats 'EUR/JPY',1,720,'1/1/2008'
--EXEC s_GetBarStats 'USDOLLAR',1,720,'1/1/2008'