CREATE FUNCTION [dbo].[clrSplit]
(@text NVARCHAR (MAX) NULL, @separator NVARCHAR (MAX) NULL)
RETURNS 
     TABLE (
        [Value] NVARCHAR (4000) NULL)
AS
 EXTERNAL NAME [SQL_DateFuncs].[UserDefinedFunctions].[clrSplit]

