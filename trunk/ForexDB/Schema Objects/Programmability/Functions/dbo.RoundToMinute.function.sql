CREATE FUNCTION [dbo].[RoundToMinute]
(@date DATETIME, @period TINYINT)
RETURNS DATETIME
AS
 EXTERNAL NAME [SQLCLR].[UserDefinedFunctions].[RoundToMinute]



















