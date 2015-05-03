CREATE TABLE [dbo].[t_Bar] (
    [Pair]           NCHAR (10)         NOT NULL,
    [Period]         INT                NOT NULL,
    [StartDate]      DATETIMEOFFSET (7) NOT NULL,
    [AskHigh]        REAL               NOT NULL,
    [AskLow]         REAL               NOT NULL,
    [AskOpen]        REAL               NOT NULL,
    [AskClose]       REAL               NOT NULL,
    [BidHigh]        REAL               NOT NULL,
    [BidLow]         REAL               NOT NULL,
    [BidOpen]        REAL               NOT NULL,
    [BidClose]       REAL               NOT NULL,
    [Volume]         INT                CONSTRAINT [DF_t_Bar_Volume] DEFAULT ((0)) NOT NULL,
    [ID]             INT                IDENTITY (1, 1) NOT NULL,
    [StartDateLocal] AS                 (CONVERT([datetime],[startdate],(0))) PERSISTED,
    [Row]            INT                NOT NULL,
    CONSTRAINT [PK_t_Bar] PRIMARY KEY NONCLUSTERED ([ID] ASC)
);








GO
CREATE NONCLUSTERED INDEX [IX_t_Bar_StartDateLocal]
    ON [dbo].[t_Bar]([StartDateLocal] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_t_Bar_Row]
    ON [dbo].[t_Bar]([Pair] ASC, [Period] ASC, [Row] ASC);

