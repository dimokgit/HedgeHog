CREATE FUNCTION config.WorkDayStopGraceMinutes(
)RETURNS int WITH SCHEMABINDING
AS
BEGIN
RETURN(SELECT CONVERT(int,Value) FROM config.Config WHERE Name = 'WorkDayStopGraceMinutes')
END