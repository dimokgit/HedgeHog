CREATE FUNCTION GetDayMinutes(
@Date datetimeoffset(7)
)RETURNS int WITH SCHEMABINDING AS
BEGIN
RETURN(
SELECT DATEDIFF(n,dbo.GetDate(@Date),@Date)
)
END