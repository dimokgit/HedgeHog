CREATE TABLE [dbo].[Punch] (
    [Id]              INT                IDENTITY (1, 1) NOT NULL,
    [Time]            DATETIMEOFFSET (7) NOT NULL,
    [DirectionId]     INT                NOT NULL,
    [TypeId]          INT                NOT NULL,
    [IsOutOfSequence] BIT                CONSTRAINT [DF_Punch_IsOutOffSequence] DEFAULT ((0)) NOT NULL,
    CONSTRAINT [PK_Punch] PRIMARY KEY NONCLUSTERED ([Id] ASC),
    CONSTRAINT [CK_Punch_Time] CHECK ([Time]>'1/1/1999'),
    CONSTRAINT [FK_Punch_PunchDirection] FOREIGN KEY ([DirectionId]) REFERENCES [dbo].[PunchDirection] ([Id]) ON DELETE NO ACTION ON UPDATE CASCADE,
    CONSTRAINT [FK_Punch_PunchType] FOREIGN KEY ([TypeId]) REFERENCES [dbo].[PunchType] ([Id]) ON DELETE NO ACTION ON UPDATE CASCADE
);






GO
CREATE UNIQUE CLUSTERED INDEX [IX_Punch_Time]
    ON [dbo].[Punch]([Time] ASC);


GO
CREATE TRIGGER [dbo].[Punch#Insert] 
   ON  [dbo].[Punch] 
   AFTER INSERT
AS 
BEGIN
SET NOCOUNT ON;

DELETE FROM PP 
FROM PunchPair PP
INNER JOIN inserted i ON PP.Start >= i.Time AND i.DirectionId = const.PunchDirectionIn()

DELETE FROM PP 
FROM PunchPair PP
INNER JOIN inserted i ON PP.Stop >= i.Time AND i.DirectionId = const.PunchDirectionOut()

EXEC sRunPunchPairs

--- Test
--BEGIN TRAN
--INSERT INTO Punch
--SELECT GETDATE(),1,1
--UNION ALL
--SELECT DATEADD(hh,4,GETDATE()),2,2
--SELECT * FROM vPunchPair
--ROLLBACK

END
GO
CREATE TRIGGER [dbo].[Punch#Delete] 
   ON  [dbo].[Punch] 
   AFTER DELETE,UPDATE
AS 
BEGIN
SET NOCOUNT ON;

DELETE FROM PP 
FROM PunchPair PP
INNER JOIN deleted d ON PP.Start >= d.Time AND d.DirectionId = const.PunchDirectionIn()

DELETE FROM PP 
FROM PunchPair PP
INNER JOIN deleted d ON PP.Stop >= d.Time AND d.DirectionId = const.PunchDirectionOut()

EXEC sRunPunchPairs

END