CREATE TABLE [dbo].[t_Session] (
    [Uid]           UNIQUEIDENTIFIER NOT NULL,
    [MinimumGross]  FLOAT (53)       NOT NULL,
    [MaximumLot]    INT              NOT NULL,
    [Profitability] FLOAT (53)       NULL,
    [Timestamp]     DATETIME         CONSTRAINT [DF_t_Session_Timestamp] DEFAULT (getdate()) NOT NULL,
    [SuperUid]      UNIQUEIDENTIFIER NULL,
    [DateMin]       DATETIME2 (7)    NULL,
    [DateMax]       DATETIME2 (7)    NULL,
    [BallanceMax]   FLOAT (53)       NULL,
    CONSTRAINT [PK_t_Session] PRIMARY KEY CLUSTERED ([Uid] ASC)
);

