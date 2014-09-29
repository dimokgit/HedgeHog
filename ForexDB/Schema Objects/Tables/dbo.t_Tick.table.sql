CREATE TABLE [dbo].[t_Tick] (
    [Pair]      VARCHAR (7) NOT NULL,
    [StartDate] DATETIME    NOT NULL,
    [Ask]       FLOAT       NOT NULL,
    [Bid]       FLOAT       NOT NULL,
    [ID]        INT         IDENTITY (1, 1) NOT NULL
);

