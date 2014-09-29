CREATE TABLE [dbo].[TradingAccount] (
    [Password]         NVARCHAR (50)    NOT NULL,
    [MasterId]         NVARCHAR (50)    NULL,
    [IsDemo]           BIT              NOT NULL,
    [AccountId]        NVARCHAR (50)    NULL,
    [Id]               UNIQUEIDENTIFIER ROWGUIDCOL NOT NULL,
    [IsMaster]         BIT              DEFAULT ((0)) NOT NULL,
    [TradeRatio]       NVARCHAR (6)     DEFAULT ('1:1') NOT NULL,
    [Commission]       FLOAT (53)       DEFAULT ((0)) NOT NULL,
    [IsActive]         BIT              DEFAULT ((1)) NOT NULL,
    [TradingMacroName] NVARCHAR (64)    DEFAULT ('Case 01') NOT NULL,
    [PipsToExit]       FLOAT (53)       NULL,
    CONSTRAINT [PK__TradingAccount__000000000000004D] PRIMARY KEY CLUSTERED ([Id] ASC)
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UQ__TradingAccount__0000000000000045]
    ON [dbo].[TradingAccount]([Id] ASC);

