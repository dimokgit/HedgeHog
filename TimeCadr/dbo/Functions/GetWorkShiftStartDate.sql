CREATE FUNCTION [dbo].[GetWorkShiftStartDate](
@Date datetimeoffset(7)
)RETURNS datetime WITH SCHEMABINDING AS
BEGIN
RETURN DATEADD(d,0,DATEDIFF(d,0,@Date))+CASE WHEN dbo.GetDayMinutes(@Date)>= 1440-config.WorkDayStartGraceMinutes() THEN 1 ELSE 0 END
END