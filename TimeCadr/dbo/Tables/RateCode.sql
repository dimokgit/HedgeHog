CREATE TABLE [dbo].[RateCode] (
    [Id]   INT          IDENTITY (1, 1) NOT NULL,
    [Name] VARCHAR (50) NULL,
    [Rate] FLOAT (53)   NOT NULL,
    CONSTRAINT [PK_RateCode] PRIMARY KEY CLUSTERED ([Id] ASC)
);

