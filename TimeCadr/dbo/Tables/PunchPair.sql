CREATE TABLE [dbo].[PunchPair] (
    [Start] DATETIMEOFFSET (7) NOT NULL,
    [Stop]  DATETIMEOFFSET (7) NOT NULL,
    CONSTRAINT [PK_PunchPair_Start] PRIMARY KEY CLUSTERED ([Start] ASC),
    CONSTRAINT [FK_PunchPair_Punch] FOREIGN KEY ([Start]) REFERENCES [dbo].[Punch] ([Time]) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT [FK_PunchPair_Punch1] FOREIGN KEY ([Stop]) REFERENCES [dbo].[Punch] ([Time]) ON DELETE NO ACTION ON UPDATE NO ACTION
);


GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch];


GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch1];




GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch];


GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch1];




GO

GO
CREATE TRIGGER [dbo].[PunchPair#Delete] 
   ON  [dbo].[PunchPair] 
   AFTER INSERT,UPDATE,DELETE
AS 
BEGIN --1
IF @@ROWCOUNT = 0 RETURN
SET NOCOUNT ON;

DELETE FROM WS
FROM WorkShift WS
INNER JOIN deleted d ON d.Stop = WS.Start 

END
GO
CREATE UNIQUE NONCLUSTERED INDEX [IX_PunchPair_Stop]
    ON [dbo].[PunchPair]([Stop] ASC);

