CREATE TABLE [dbo].[t_TradeValue] (
    [Id]      INT           IDENTITY (1, 1) NOT NULL,
    [TradeId] NVARCHAR (32) NOT NULL,
    [Name]    VARCHAR (128) NOT NULL,
    [Value]   VARCHAR (256) NOT NULL,
    CONSTRAINT [PK_t_TradeValue] PRIMARY KEY NONCLUSTERED ([Id] ASC),
    CONSTRAINT [FK_t_TradeValue_t_Trade] FOREIGN KEY ([TradeId]) REFERENCES [dbo].[t_Trade] ([Id]) ON DELETE CASCADE
);

