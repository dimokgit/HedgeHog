CREATE FUNCTION [dbo].[clrSplitTwo]
(@text NVARCHAR (4000), @separator NVARCHAR (4000))
RETURNS 
     TABLE (
        [Value1] NVARCHAR (2000) NULL,
        [Value2] NVARCHAR (2000) NULL)
AS
 EXTERNAL NAME [SQL_DateFuncs].[UserDefinedFunctions].[clrSplitTwo]







