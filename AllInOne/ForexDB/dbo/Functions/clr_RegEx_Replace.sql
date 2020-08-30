CREATE FUNCTION [dbo].[clr_RegEx_Replace]
(@text NVARCHAR (MAX) NULL, @pattern NVARCHAR (4000) NULL, @replacement NVARCHAR (4000) NULL, @count INT NULL, @startAt INT NULL, @ignoreCase BIT NULL)
RETURNS NVARCHAR (MAX)
AS
 EXTERNAL NAME [LightLib].[UserDefinedFunctions].[clr_RegEx_Replace]


GO
EXECUTE sp_addextendedproperty @name = N'AutoDeployed', @value = N'yes', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'FUNCTION', @level1name = N'clr_RegEx_Replace';


GO
EXECUTE sp_addextendedproperty @name = N'SqlAssemblyFileLine', @value = 9, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'FUNCTION', @level1name = N'clr_RegEx_Replace';

