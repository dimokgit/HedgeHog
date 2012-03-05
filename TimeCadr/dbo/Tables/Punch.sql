CREATE TABLE [dbo].[Punch] (
    [Id]              INT                IDENTITY (1, 1) NOT NULL,
    [Time]            DATETIMEOFFSET (7) NOT NULL,
    [DirectionId]     INT                NOT NULL,
    [TypeId]          INT                NOT NULL,
    [IsOutOfSequence] BIT                CONSTRAINT [DF_Punch_IsOutOffSequence] DEFAULT ((0)) NOT NULL,
    [TimeUTC]         AS                 (switchoffset([Time],(0))) PERSISTED,
    [TimeZoneOffset]  AS                 (datepart(tzoffset,[Time])) PERSISTED,
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
   AFTER INSERT,UPDATE,DELETE
AS 
BEGIN --1
IF @@ROWCOUNT = 0 RETURN
SET NOCOUNT ON;

IF dbo.fn_ColumnsUpdated(COLUMNS_UPDATED(),@@PROCID) = 'IsOutOfSequence' RETURN

DECLARE @Times TABLE(Time datetimeoffset)

-- Recalculate IsOutOfSequence
UPDATE Punch SET IsOutOfSequence = PU.IsOutOfSequence
OUTPUT inserted.Time INTO @Times
FROM Punch INNER JOIN
(
SELECT Prev.Time,CASE WHEN Prev.DirectionId = P.DirectionId THEN 1 ELSE 0 END IsOutOfSequence
FROM Punch P CROSS APPLY getPunchPrev(P.Time)Prev
)
PU ON Punch.Time = PU.Time AND Punch.IsOutOfSequence <> PU.IsOutOfSequence

DECLARE @D datetimeoffset

;WITH D AS
(
SELECT Time FROM inserted
UNION
SELECT Time FROM deleted
UNION
SELECT Time FROM @Times 
)
SELECT @D = MIN(Time) FROM
(
SELECT Time FROM D
UNION
SELECT PP.Start FROM D CROSS APPLY getPunchPairPrev(D.Time)PP
)T

--- Clean PunchPairs
DELETE FROM PP 
FROM PunchPair PP
WHERE PP.Stop >= @D

--- Chained actions
EXEC sRunPunchPairs
--- Test

END
GO
