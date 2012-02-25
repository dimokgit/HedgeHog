CREATE TABLE [dbo].[PunchPair] (
    [Start] DATETIMEOFFSET (7) NOT NULL,
    [Stop]  DATETIMEOFFSET (7) NOT NULL,
    CONSTRAINT [PK_PunchPair] PRIMARY KEY CLUSTERED ([Start] ASC),
    CONSTRAINT [FK_PunchPair_Punch] FOREIGN KEY ([Start]) REFERENCES [dbo].[Punch] ([Time]) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT [FK_PunchPair_Punch1] FOREIGN KEY ([Stop]) REFERENCES [dbo].[Punch] ([Time]) ON DELETE NO ACTION ON UPDATE NO ACTION
);


GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch];


GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch1];




GO
CREATE TRIGGER [dbo].[PunchPair#DUI] 
   ON  [dbo].[PunchPair] 
   AFTER DELETE,UPDATE,INSERT
AS 
BEGIN
SET NOCOUNT ON;


DELETE FROM WS 
FROM WorkShift WS
--INNER JOIN inserted i ON WS.Start >= i.Start

EXEC sRunPunchPairs
EXEC sRunWorkShifts

END
GO
CREATE TRIGGER [dbo].[PunchPair#Delete] 
   ON  [dbo].[PunchPair] 
   AFTER DELETE
AS 
BEGIN
SET NOCOUNT ON;


DELETE FROM WS 
FROM WorkShift WS
INNER JOIN deleted d ON WS.Start >= d.Start

EXEC sRunPunchPairs
EXEC sRunWorkShifts

END