CREATE FUNCTION EOWEEK(
  @Date datetimeoffset
)RETURNS datetimeoffset AS
BEGIN
DECLARE @D datetimeoffset = DATEADD(dd,7 - DATEPART(dw,@Date),@Date)
RETURN DATEADD(dd,0,DATEDIFF(dd,0,@D))
END