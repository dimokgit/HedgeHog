CREATE TABLE [dbo].[RateCodeType] (
    [Id]       INT          IDENTITY (1, 1) NOT NULL,
    [Name]     VARCHAR (50) NOT NULL,
    [Priority] INT          NOT NULL,
    CONSTRAINT [PK_RateCodeType] PRIMARY KEY CLUSTERED ([Id] ASC)
);





