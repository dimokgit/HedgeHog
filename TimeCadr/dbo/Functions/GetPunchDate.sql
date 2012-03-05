CREATE FUNCTION GetPunchDate(
@Date datetimeoffset(7),
@HourMax int = 23,
@MinuteMax int = 0
)RETURNS datetime WITH SCHEMABINDING AS
BEGIN
RETURN DATEADD(d,0,DATEDIFF(d,0,@Date))+CASE WHEN DATEPART(hh,@Date)>=@HourMax AND DATEPART(n,@Date)>=@MinuteMax THEN 1 ELSE 0 END
END