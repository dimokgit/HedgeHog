CREATE FUNCTION CalcHourMinute(
  @Minute int
)RETURNS int AS BEGIN
RETURN (@Minute) % 60
END