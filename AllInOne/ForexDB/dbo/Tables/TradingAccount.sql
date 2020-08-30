CREATE TABLE [dbo].[TradingAccount] (
    [Password]         NVARCHAR (50)    NOT NULL,
    [MasterId]         NVARCHAR (50)    NULL,
    [IsDemo]           BIT              NOT NULL,
    [AccountId]        NVARCHAR (50)    NULL,
    [Id]               UNIQUEIDENTIFIER CONSTRAINT [DF_TradingAccount_Id] DEFAULT (newid()) NOT NULL,
    [IsMaster]         BIT              DEFAULT ((0)) NOT NULL,
    [TradeRatio]       NVARCHAR (6)     DEFAULT ('1:1') NOT NULL,
    [Commission]       FLOAT (53)       DEFAULT ((0)) NOT NULL,
    [IsActive]         BIT              DEFAULT ((1)) NOT NULL,
    [TradingMacroName] NVARCHAR (64)    DEFAULT ('Case 01') NOT NULL,
    [PipsToExit]       FLOAT (53)       NULL,
    [Currency]         CHAR (3)         CONSTRAINT [DF_TradingAccount_Currency] DEFAULT ('USD') NOT NULL,
    CONSTRAINT [PK__TradingAccount__000000000000004D] PRIMARY KEY CLUSTERED ([Id] ASC)
);

