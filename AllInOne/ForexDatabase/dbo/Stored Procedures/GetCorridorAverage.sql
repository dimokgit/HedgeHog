CREATE PROCEDURE [dbo].[GetCorridorAverage]
	@Pair varchar(7),
	@Period tinyint,
	@CorridorDate datetime,
	@CorridorPeriods int,
	@BarMinutes int
AS
SELECT * FROM CorridorAverage(@Pair,@Period,@CorridorDate,@CorridorPeriods,@BarMinutes)