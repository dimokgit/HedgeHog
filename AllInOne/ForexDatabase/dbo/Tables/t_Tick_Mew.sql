CREATE TABLE [dbo].[t_Tick_Mew] (
    [ID]   INT         IDENTITY (1, 1) NOT NULL,
    [Pair] VARCHAR (7) NOT NULL,
    [Time] DATETIME    NOT NULL,
    [Ask]  FLOAT (53)  NOT NULL,
    [Bid]  FLOAT (53)  NOT NULL,
    CONSTRAINT [PK_t_Tick_Mew] PRIMARY KEY CLUSTERED ([ID] ASC)
);

