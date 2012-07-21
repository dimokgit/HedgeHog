CREATE FUNCTION [dbo].[Date]
(@date DATETIME)
RETURNS DATETIME
AS
 EXTERNAL NAME [SQLCLR].[UserDefinedFunctions].[Date]





