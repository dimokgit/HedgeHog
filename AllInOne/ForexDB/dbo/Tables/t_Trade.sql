CREATE TABLE [dbo].[t_Trade] (
    [Id]                   NVARCHAR (32)    NOT NULL,
    [Buy]                  BIT              NOT NULL,
    [PL]                   FLOAT (53)       NOT NULL,
    [GrossPL]              FLOAT (53)       NOT NULL,
    [Lot]                  FLOAT (53)       NOT NULL,
    [Pair]                 NVARCHAR (8)     NOT NULL,
    [TimeOpen]             DATETIME         NOT NULL,
    [TimeClose]            DATETIME         NOT NULL,
    [AccountId]            NVARCHAR (16)    NOT NULL,
    [Commission]           FLOAT (53)       NOT NULL,
    [IsVirtual]            BIT              NOT NULL,
    [TimeStamp]            DATETIME         CONSTRAINT [DF_t_Trade_TimeStamp] DEFAULT (getdate()) NULL,
    [CorridorHeightInPips] FLOAT (53)       NOT NULL,
    [CorridorMinutesBack]  FLOAT (53)       NOT NULL,
    [SessionId]            UNIQUEIDENTIFIER NOT NULL,
    [PriceOpen]            FLOAT (53)       CONSTRAINT [DF_t_Trade_PriceOpen] DEFAULT ((0)) NOT NULL,
    [PriceClose]           FLOAT (53)       CONSTRAINT [DF_t_Trade_PriceClose] DEFAULT ((0)) NOT NULL,
    [SessionInfo]          NVARCHAR (4000)  NULL,
    [RunningBalance]       FLOAT (53)       NULL,
    [RunningBalanceTotal]  FLOAT (53)       NULL,
    [SessionInfo2]         VARCHAR (1024)   NULL,
    CONSTRAINT [PK_t_Trade] PRIMARY KEY CLUSTERED ([Id] ASC)
);


GO
CREATE TRIGGER [dbo].[triTrade] 
   ON  dbo.t_Trade 
   AFTER INSERT
AS 
BEGIN
	SET NOCOUNT ON;

DECLARE @SID uniqueidentifier = (SELECT SessionId FROM inserted)
EXEC ProcessTrades @SID
END
