CREATE PROCEDURE [dbo].[GetCorridor]
	@Pair varchar(7),
	@Period tinyint,
	@Date datetime,
	@SpreadPeriod int
AS
SELECT dbo.Corridor(	@Pair,	@Period,	@Date ,	@SpreadPeriod )