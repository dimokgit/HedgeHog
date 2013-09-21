CREATE FUNCTION [dbo].[clr_RegEx_Replace]
(@text NVARCHAR (MAX), @pattern NVARCHAR (4000), @replacement NVARCHAR (4000), @count INT, @startAt INT, @ignoreCase BIT)
RETURNS NVARCHAR (MAX)
AS
 EXTERNAL NAME [LightLib].[UserDefinedFunctions].[clr_RegEx_Replace]


















GO
EXECUTE sp_addextendedproperty @name = N'AutoDeployed', @value = N'yes', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'FUNCTION', @level1name = N'clr_RegEx_Replace';


GO
EXECUTE sp_addextendedproperty @name = N'SqlAssemblyFileLine', @value = 9, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'FUNCTION', @level1name = N'clr_RegEx_Replace';

