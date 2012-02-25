CREATE PROCEDURE sRunWorkShifts AS

EXEC sRunWorkShift
WHILE @@ROWCOUNT > 0
EXEC sRunWorkShift