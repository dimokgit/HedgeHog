CREATE FUNCTION [config].[AutoMealBreak](
)RETURNS int WITH SCHEMABINDING
AS
BEGIN
RETURN(SELECT CONVERT(int,Value) FROM config.Config WHERE Name = 'AutoMealBreak')
END