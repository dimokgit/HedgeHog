CREATE TABLE [dbo].[t_Tick] (
    [Pair]      VARCHAR (7) NOT NULL,
    [StartDate] DATETIME    NOT NULL,
    [Ask]       FLOAT (53)  NOT NULL,
    [Bid]       FLOAT (53)  NOT NULL,
    [ID]        INT         IDENTITY (1, 1) NOT NULL,
    CONSTRAINT [PK_t_Tick_1] PRIMARY KEY CLUSTERED ([ID] ASC)
);

