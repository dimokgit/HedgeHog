﻿CREATE FUNCTION [config].[MealBreakDeductionStart](
)RETURNS int WITH SCHEMABINDING
AS
BEGIN
RETURN(SELECT CONVERT(int,Value) FROM config.Config WHERE Name = 'MealBreakDeductionStart')
END