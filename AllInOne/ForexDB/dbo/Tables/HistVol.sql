CREATE TABLE [dbo].[HistVol] (
    [Pair]      VARCHAR (3)        NOT NULL,
    [StartDate] DATETIMEOFFSET (7) NOT NULL,
    [Delta]     FLOAT (53)         NULL,
    [Price]     REAL               NULL,
    [DailyHV]   FLOAT (53)         NULL,
    [Count]     INT                NOT NULL,
    CONSTRAINT [PK_HistVol] PRIMARY KEY CLUSTERED ([Pair] ASC, [StartDate] ASC, [Count] ASC)
);

