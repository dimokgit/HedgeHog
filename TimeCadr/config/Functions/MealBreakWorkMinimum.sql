CREATE FUNCTION [config].[MealBreakWorkMinimum](
)RETURNS int WITH SCHEMABINDING
AS
BEGIN
RETURN(SELECT CONVERT(int,Value) FROM config.Config WHERE Name = 'MealBreakWorkMinimum')
END