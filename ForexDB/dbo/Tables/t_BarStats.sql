CREATE TABLE [dbo].[t_BarStats] (
    [Pair]           VARCHAR (10)       NOT NULL,
    [Period]         INT                NOT NULL,
    [Length]         INT                NOT NULL,
    [StartDate]      DATETIMEOFFSET (7) NOT NULL,
    [StopDate]       DATETIMEOFFSET (7) NULL,
    [BarsHeight]     FLOAT (53)         NULL,
    [PriceHeight]    FLOAT (53)         NULL,
    [PriceStDev]     FLOAT (53)         NULL,
    [Distance]       FLOAT (53)         NULL,
    [StartDateLocal] AS                 (CONVERT([datetime],[StartDate],(0))) PERSISTED,
    [StopDateLocal]  AS                 (CONVERT([datetime],[StopDate],(0))) PERSISTED
);


GO
CREATE UNIQUE CLUSTERED INDEX [IX_t_BarStats]
    ON [dbo].[t_BarStats]([Pair] ASC, [Period] ASC, [Length] ASC, [StartDateLocal] ASC);

