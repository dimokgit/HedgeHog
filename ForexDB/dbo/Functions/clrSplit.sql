CREATE FUNCTION [dbo].[clrSplit]
(@text NVARCHAR (4000), @separator NVARCHAR (4000))
RETURNS 
     TABLE (
        [Value] NVARCHAR (4000) NULL)
AS
 EXTERNAL NAME [SQL_DateFuncs].[UserDefinedFunctions].[clrSplit]

