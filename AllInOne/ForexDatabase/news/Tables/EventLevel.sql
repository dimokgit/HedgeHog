CREATE TABLE [news].[EventLevel] (
    [Id]    VARCHAR (1)  NOT NULL,
    [Name]  VARCHAR (10) NOT NULL,
    [Level] INT          CONSTRAINT [DF_EventLevel_Level] DEFAULT ((0)) NOT NULL,
    CONSTRAINT [PK_EventLevel] PRIMARY KEY CLUSTERED ([Id] ASC)
);

