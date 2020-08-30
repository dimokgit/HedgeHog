CREATE FUNCTION [dbo].[GetMaximas](
@DateStart datetime,
@DateEnd datetime,
@TimeFrame int
)RETURNS @T TABLE(StartDate datetime,Volts float,Price float)
BEGIN
DECLARE @Volts AS dt_VoltsTable

INSERT INTO @Volts
SELECT * FROM t_Tick_Volts WHERE 
StartDate BETWEEN @DateStart AND @DateEnd AND NOT Volts IS NULL 

INSERT INTO @T
SELECT * FROM FindMaximas(@TimeFrame,@Volts)

RETURN
--TRUNCATE TABLE t_TickMaxima
--INSERT INTO t_TickMaxima
--SELECT * 
--FROM GetMaximas('2009-07-20 00:00:00','2009-07-21 00:00:00',30)
END