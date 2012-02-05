CREATE TABLE [dbo].[PunchType] (
    [Id]   INT          IDENTITY (1, 1) NOT NULL,
    [Name] VARCHAR (30) NOT NULL,
    CONSTRAINT [PK_PunchType] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [IX_PunchType_Name] UNIQUE NONCLUSTERED ([Name] ASC)
);

