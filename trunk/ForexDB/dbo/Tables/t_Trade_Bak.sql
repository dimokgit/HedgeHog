CREATE TABLE [dbo].[t_Trade_Bak] (
    [Id]                   NVARCHAR (16)    NOT NULL,
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
    [TimeStamp]            DATETIME         NULL,
    [CorridorHeightInPips] FLOAT (53)       NOT NULL,
    [CorridorMinutesBack]  FLOAT (53)       NOT NULL,
    [SessionId]            UNIQUEIDENTIFIER NOT NULL,
    [PriceOpen]            FLOAT (53)       NOT NULL,
    [PriceClose]           FLOAT (53)       NOT NULL,
    [SessionInfo]          NVARCHAR (4000)  NULL,
    [RunningBalance]       FLOAT (53)       NULL,
    [RunningBalanceTotal]  FLOAT (53)       NULL,
    [SessionInfo2]         VARCHAR (1024)   NULL
);

