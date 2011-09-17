CREATE PROCEDURE GetCorridor
	@Pair varchar(7),
	@Period tinyint,
	@Date datetime,
	@SpreadPeriod int
AS
SELECT dbo.Corridor(	@Pair,	@Period,	@Date ,	@SpreadPeriod )
