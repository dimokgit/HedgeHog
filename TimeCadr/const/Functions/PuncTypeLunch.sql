﻿CREATE FUNCTION [const].[PuncTypeLunch](
)RETURNS int WITH SCHEMABINDING
AS
BEGIN
RETURN(SELECT Id FROM dbo.PunchType WHERE Name = 'Lunch')
END