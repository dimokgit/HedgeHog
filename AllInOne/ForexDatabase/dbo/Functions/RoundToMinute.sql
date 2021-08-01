CREATE FUNCTION [dbo].[RoundToMinute]
(@date DATETIME NULL, @period TINYINT NULL)
RETURNS DATETIME
AS
 EXTERNAL NAME [SQLCLR].[UserDefinedFunctions].[RoundToMinute]

