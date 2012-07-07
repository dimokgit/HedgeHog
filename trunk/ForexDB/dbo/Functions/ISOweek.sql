CREATE FUNCTION [dbo].[ISOweek] (@DATE datetime)
RETURNS int
WITH EXECUTE AS CALLER
AS
BEGIN
DECLARE @WeekOfMonth TINYINT
SET @WeekOfMonth = (DAY(@DATE) + 
(DATEPART(dw, DATEADD (MONTH, DATEDIFF (MONTH, 0, @DATE), 0)) 
--^-- The day of the week for the first day of month
-1) -- # of days to add to make the first week full 7 days
-1)/7 + 1 
RETURN(@WeekOfMonth)
END;