CREATE FUNCTION [const].[PunchDirectionOut](
)RETURNS int WITH SCHEMABINDING
AS
BEGIN
RETURN(SELECT Id FROM dbo.PunchDirection WHERE Name = 'Out')
END