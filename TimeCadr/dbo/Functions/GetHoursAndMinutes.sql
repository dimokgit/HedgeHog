CREATE FUNCTION GetHoursAndMinutes(
  @TotalMinutes int
)RETURNS @TM TABLE(Hour int,Minute int) AS
BEGIN

WHILE @TotalMinutes>0 BEGIN

INSERT INTO @TM
--SELECT @TotalMinutes/60 + 1 Hour,@TotalMinutes % 60 Minute--,@TotalMinutes TotalMinutes
SELECT DATEPART(hh,DATEADD(n,@TotalMinutes,0))+1,DATEPART(n,DATEADD(n,@TotalMinutes,0))
SET @TotalMinutes = @TotalMinutes - 1

UPDATE @TM SET Hour = Hour-1,Minute = 60 WHERE Minute = 0

End

RETURN

END