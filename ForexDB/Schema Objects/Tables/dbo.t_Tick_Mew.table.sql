CREATE TABLE [dbo].[t_Tick_Mew] (
    [ID]   INT         IDENTITY (1, 1) NOT NULL,
    [Pair] VARCHAR (7) NOT NULL,
    [Time] DATETIME    NOT NULL,
    [Ask]  FLOAT       NOT NULL,
    [Bid]  FLOAT       NOT NULL
);

