CREATE FUNCTION [dbo].[Time]
(@date DATETIME)
RETURNS DATETIME
AS
 EXTERNAL NAME [SQLCLR].[UserDefinedFunctions].[Time]

















