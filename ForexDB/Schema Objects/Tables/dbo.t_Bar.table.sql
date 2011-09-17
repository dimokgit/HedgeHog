CREATE TABLE [dbo].[t_Bar] (
    [Pair]      NCHAR (10)         NOT NULL,
    [Period]    INT                NOT NULL,
    [StartDate] DATETIMEOFFSET (7) NOT NULL,
    [AskHigh]   REAL               NOT NULL,
    [AskLow]    REAL               NOT NULL,
    [AskOpen]   REAL               NOT NULL,
    [AskClose]  REAL               NOT NULL,
    [BidHigh]   REAL               NOT NULL,
    [BidLow]    REAL               NOT NULL,
    [BidOpen]   REAL               NOT NULL,
    [BidClose]  REAL               NOT NULL,
    [Volume]    INT                NOT NULL,
    [ID]        INT                IDENTITY (1, 1) NOT NULL
);

