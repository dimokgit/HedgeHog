CREATE TABLE [dbo].[WorkShift] (
    [Start] DATETIMEOFFSET (7) NOT NULL,
    [Stop]  DATETIMEOFFSET (7) NOT NULL,
    CONSTRAINT [PK_Shift] PRIMARY KEY CLUSTERED ([Start] ASC),
    CONSTRAINT [FK_WorkShift_PunchPair] FOREIGN KEY ([Start]) REFERENCES [dbo].[PunchPair] ([Start]) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT [FK_WorkShift_PunchPair1] FOREIGN KEY ([Stop]) REFERENCES [dbo].[PunchPair] ([Stop]) ON DELETE CASCADE ON UPDATE NO ACTION
);


GO
ALTER TABLE [dbo].[WorkShift] NOCHECK CONSTRAINT [FK_WorkShift_PunchPair];




GO
CREATE TRIGGER WorkShift#Insert
   ON  WorkShift 
   AFTER INSERT,UPDATE,DELETE
AS 
IF @@ROWCOUNT = 0 RETURN
SET NOCOUNT ON;

INSERT INTO WorkShiftMinute(WorkShiftStart,Hour,Minute)
SELECT WS.Start,HM.Hour,HM.Minute FROM inserted i
INNER JOIN vWorkShift WS ON WS.Start = i.Start
CROSS APPLY GetHoursAndMinutes(WS.TotalMinutes)HM ORDER BY WS.Start,HM.Hour,HM.Minute