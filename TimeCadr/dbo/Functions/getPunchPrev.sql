CREATE FUNCTION [dbo].[getPunchPrev](
@Time datetimeoffset
)RETURNS TABLE AS
RETURN
(
SELECT TOP 1 * FROM Punch WHERE Time <@Time ORDER BY Time DESC
)