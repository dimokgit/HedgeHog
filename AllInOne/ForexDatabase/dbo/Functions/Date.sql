﻿CREATE FUNCTION [dbo].[Date]
(@date DATETIME NULL)
RETURNS DATETIME
AS
 EXTERNAL NAME [SQLCLR].[UserDefinedFunctions].[Date]

