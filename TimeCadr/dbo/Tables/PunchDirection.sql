CREATE TABLE [dbo].[PunchDirection] (
    [Id]   INT          IDENTITY (1, 1) NOT NULL,
    [Name] VARCHAR (20) NOT NULL,
    CONSTRAINT [PK_PunchDirection] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [IX_PunchDirection_Name] UNIQUE NONCLUSTERED ([Name] ASC)
);

