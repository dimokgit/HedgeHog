CREATE FUNCTION config.WorkDayStartGraceMinutes(
)RETURNS int WITH SCHEMABINDING
AS
BEGIN
RETURN(SELECT CONVERT(int,Value) FROM config.Config WHERE Name = 'WorkDayStartGraceMinutes')
END