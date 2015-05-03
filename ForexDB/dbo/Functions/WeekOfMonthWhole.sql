CREATE FUNCTION WeekOfMonthWhole(
@date datetime
)RETURNS TABLE RETURN(
select datepart(day, datediff(day, 0, @date)/7 * 7)/7 + 1 Week
)