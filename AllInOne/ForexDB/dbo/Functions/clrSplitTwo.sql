CREATE FUNCTION [dbo].[clrSplitTwo]
(@text NVARCHAR (MAX) NULL, @separator NVARCHAR (MAX) NULL)
RETURNS 
     TABLE (
        [Value1] NVARCHAR (2000) NULL,
        [Value2] NVARCHAR (2000) NULL)
AS
 EXTERNAL NAME [SQL_DateFuncs].[UserDefinedFunctions].[clrSplitTwo]

