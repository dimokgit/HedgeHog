CREATE TABLE [dbo].[t_Price] (
    [ID]        INT            IDENTITY (1, 1) NOT NULL,
    [Account]   VARCHAR (16)   NOT NULL,
    [Pair]      VARCHAR (7)    NOT NULL,
    [Date]      DATETIME       NOT NULL,
    [Ask]       NUMERIC (9, 5) NOT NULL,
    [Bid]       NUMERIC (9, 5) NOT NULL,
    [Speed]     NUMERIC (5, 1) NOT NULL,
    [Spread]    NUMERIC (5, 1) NOT NULL,
    [Power]     NUMERIC (5, 1) NOT NULL,
    [Row]       NUMERIC (5, 1) NOT NULL,
    [IsBuySell] INT            NOT NULL
);

