CREATE FUNCTION [dbo].[ToDateTimeOffset]
(@date DATETIME NULL)
RETURNS DATETIMEOFFSET (7)
AS
 EXTERNAL NAME [SQL_DateFuncs].[UserDefinedFunctions].[ToDateTimeOffset]

