CREATE PROCEDURE dbo.s_Bars
	(@Pair varchar(7),
	@Period tinyint
)AS
SET NOCOUNT ON
SELECT * FROM Bars(@Pair,@Period)