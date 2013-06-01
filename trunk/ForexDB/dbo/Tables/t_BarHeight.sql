CREATE TABLE [dbo].[t_BarHeight] (
    [Pair]        VARCHAR (30)       NOT NULL,
    [Period]      INT                NOT NULL,
    [Length]      INT                NOT NULL,
    [StartDate]   DATETIMEOFFSET (7) NOT NULL,
    [AvgHeight]   FLOAT (53)         NOT NULL,
    [StDevHeight] FLOAT (53)         CONSTRAINT [DF_t_BarHeight_StDevHeight] DEFAULT ((0)) NOT NULL,
    CONSTRAINT [PK_t_BarHeight] PRIMARY KEY CLUSTERED ([Pair] ASC, [Period] ASC, [Length] ASC, [StartDate] ASC)
);

