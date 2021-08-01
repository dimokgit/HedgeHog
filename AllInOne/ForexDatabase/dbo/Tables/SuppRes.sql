CREATE TABLE [dbo].[SuppRes] (
    [Rate]           FLOAT (53)       NOT NULL,
    [IsSupport]      BIT              NOT NULL,
    [TradingMacroID] UNIQUEIDENTIFIER NOT NULL,
    [UID]            UNIQUEIDENTIFIER NOT NULL,
    [TradesCount]    FLOAT (53)       DEFAULT ((1)) NOT NULL,
    CONSTRAINT [PK__SuppRes__0000000000000906] PRIMARY KEY CLUSTERED ([UID] ASC)
);

