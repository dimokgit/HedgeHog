﻿CREATE FUNCTION [const].[PunchDirectionIn](
)RETURNS int WITH SCHEMABINDING
AS
BEGIN
RETURN(SELECT Id FROM dbo.PunchDirection WHERE Name = 'In')
END