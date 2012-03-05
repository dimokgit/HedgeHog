CREATE TABLE [config].[Config] (
    [Id]    INT             IDENTITY (1, 1) NOT NULL,
    [Name]  VARCHAR (128)   NOT NULL,
    [Value] NVARCHAR (4000) NOT NULL,
    CONSTRAINT [PK_Config] PRIMARY KEY CLUSTERED ([Id] ASC)
);

