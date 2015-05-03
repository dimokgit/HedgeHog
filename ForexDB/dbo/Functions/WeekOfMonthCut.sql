CREATE FUNCTION WeekOfMonthCut(
  @date datetime
)RETURNS TABLE RETURN(
select datediff(week, dateadd(week, datediff(week, 0, dateadd(month, datediff(month, 0, @date), 0)), 0), @date - 1) + 1 Week
)