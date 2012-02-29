CREATE PROCEDURE sRunWorkShift AS

DECLARE @SiftTimeout int SET @SiftTimeout = const.ShiftTimeout()
DECLARE @LastShiftTime datetimeoffset SELECT  @LastShiftTime = ISNULL(MAX(Stop),'1/1/1900') FROM WorkShift

;WITH Shifts AS
(
SELECT TOP 1 PP.Start,PP.Stop FROM PunchPair PP WHERE PP.Start>@LastShiftTime
UNION ALL
SELECT PP.Start,PP.Stop FROM PunchPair PP
INNER JOIN Shifts S ON PP.Start > S.Stop AND  DATEDIFF(n,S.Stop,PP.Start)<=@SiftTimeout
)

INSERT INTO WorkShift
SELECT * FROM
(
SELECT 
	(SELECT TOP 1 Start FROM Shifts) Start,
	(SELECT TOP 1 Stop FROM Shifts ORDER BY Stop DESC) Stop
)T WHERE NOT Stop IS NULL

---Test
--BEGIN TRAN
--DELETE FROM WorkShift
--EXEC sRunWorkShift
--WHILE @@ROWCOUNT > 0
--EXEC sRunWorkShift
--SELECT * FROM WorkShift
--ROLLBACK