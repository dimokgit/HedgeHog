CREATE TABLE [dbo].[t_Trade] (
    [Id]                   NVARCHAR (16)    NOT NULL,
    [Buy]                  BIT              NOT NULL,
    [PL]                   FLOAT            NOT NULL,
    [GrossPL]              FLOAT            NOT NULL,
    [Lot]                  FLOAT            NOT NULL,
    [Pair]                 NVARCHAR (7)     NOT NULL,
    [TimeOpen]             DATETIME         NOT NULL,
    [TimeClose]            DATETIME         NOT NULL,
    [AccountId]            NVARCHAR (16)    NOT NULL,
    [Commission]           FLOAT            NOT NULL,
    [IsVirtual]            BIT              NOT NULL,
    [TimeStamp]            DATETIME         NULL,
    [CorridorHeightInPips] FLOAT            NOT NULL,
    [CorridorMinutesBack]  FLOAT            NOT NULL,
    [SessionId]            UNIQUEIDENTIFIER NOT NULL,
    [PriceOpen]            FLOAT            NOT NULL,
    [PriceClose]           FLOAT            NOT NULL
);

