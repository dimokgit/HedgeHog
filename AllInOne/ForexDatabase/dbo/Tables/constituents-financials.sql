CREATE TABLE [dbo].[constituents-financials] (
    [Symbol]         VARCHAR (5)   NULL,
    [Name]           VARCHAR (38)  NULL,
    [Sector]         VARCHAR (27)  NULL,
    [Price]          VARCHAR (22)  NULL,
    [Dividend Yield] REAL          NULL,
    [Price Earnings] REAL          NULL,
    [Earnings Share] REAL          NULL,
    [Book Value]     REAL          NULL,
    [52 week low]    REAL          NULL,
    [52 week high]   REAL          NULL,
    [Market Cap]     REAL          NULL,
    [EBITDA]         REAL          NULL,
    [Price Sales]    REAL          NULL,
    [Price Book]     REAL          NULL,
    [SEC Filings]    VARCHAR (128) NULL
);

