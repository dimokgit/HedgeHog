CREATE TABLE [dbo].[PunchInputMethod] (
    [Id]    INT          NOT NULL,
    [Name]  VARCHAR (64) NOT NULL,
    [Short] AS           (left([Name],(3))) PERSISTED,
    CONSTRAINT [PK_PunchInputMethod] PRIMARY KEY CLUSTERED ([Id] ASC)
);

