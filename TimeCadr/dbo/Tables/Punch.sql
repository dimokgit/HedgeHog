CREATE TABLE [dbo].[Punch] (
    [Id]          INT                IDENTITY (1, 1) NOT NULL,
    [DirectionId] INT                NOT NULL,
    [TypeId]      INT                NOT NULL,
    [Time]        DATETIMEOFFSET (7) NOT NULL,
    CONSTRAINT [PK_Punch] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Punch_PunchDirection] FOREIGN KEY ([DirectionId]) REFERENCES [dbo].[PunchDirection] ([Id]) ON DELETE NO ACTION ON UPDATE CASCADE,
    CONSTRAINT [FK_Punch_PunchType] FOREIGN KEY ([TypeId]) REFERENCES [dbo].[PunchType] ([Id]) ON DELETE NO ACTION ON UPDATE CASCADE
);

