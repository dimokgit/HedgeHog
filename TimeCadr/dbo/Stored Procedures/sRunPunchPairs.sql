CREATE PROCEDURE sRunPunchPairs AS

DECLARE @DirIn int, @DirOut int
SELECT @DirIn = const.PunchDirectionIn(),@DirOut = const.PunchDirectionOut()

DECLARE @Out TABLE(
TimeIn datetimeoffset,TimeOut datetimeoffset,TimeNextIn	datetimeoffset,TimeNextOut datetimeoffset)

INSERT INTO @Out
SELECT TOP 1 PIn.Time TimeIn,POut.Time TimeOut,PNextIn.Time TimeNextIn,PNextOut.Time TimeNextOut
FROM Punch PIn 
INNER JOIN Punch POut ON PIn.DirectionId = @DirIn AND POut.Time > PIn.Time AND POut.DirectionId = @DirOut
LEFT OUTER JOIN Punch PNextIn ON PNextIn.Time > POut.Time AND PNextIn.DirectionId = @DirIn
LEFT OUTER JOIN Punch PNextOut ON PNextOut.Time > PNextIn.Time AND PNextOut.DirectionId = @DirOut
WHERE PIn.Time > (SELECT ISNULL(MAX(Stop),'1/1/1900') FROM PunchPair)

WHILE @@ROWCOUNT > 0 BEGIN

INSERT INTO @Out
SELECT TOP 1 PIn.Time TimeIn,POut.Time TimeOut,PNextIn.Time TimeNextIn,PNextOut.Time TimeNextOut
FROM Punch PIn 
INNER JOIN Punch POut ON PIn.DirectionId = @DirIn AND POut.Time > PIn.Time AND POut.DirectionId = @DirOut
LEFT OUTER JOIN Punch PNextIn ON PNextIn.Time > POut.Time AND PNextIn.DirectionId = @DirIn
LEFT OUTER JOIN Punch PNextOut ON PNextOut.Time > PNextIn.Time AND PNextOut.DirectionId = @DirOut
WHERE PIn.Time > (SELECT MAX(TimeOut) FROM @Out)

END

INSERT INTO PunchPair(Start,Stop)
SELECT O.TimeIn,O.TimeOut FROM @Out O