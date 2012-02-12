CREATE TABLE [dbo].[Punch] (
    [Time]        DATETIMEOFFSET (7) NOT NULL,
    [DirectionId] INT                NOT NULL,
    [TypeId]      INT                NOT NULL,
    CONSTRAINT [PK_Punch_1] PRIMARY KEY CLUSTERED ([Time] ASC),
    CONSTRAINT [CK_Punch_Time] CHECK ([Time]>'1/1/1999'),
    CONSTRAINT [FK_Punch_PunchDirection] FOREIGN KEY ([DirectionId]) REFERENCES [dbo].[PunchDirection] ([Id]) ON DELETE NO ACTION ON UPDATE CASCADE,
    CONSTRAINT [FK_Punch_PunchType] FOREIGN KEY ([TypeId]) REFERENCES [dbo].[PunchType] ([Id]) ON DELETE NO ACTION ON UPDATE CASCADE
);



