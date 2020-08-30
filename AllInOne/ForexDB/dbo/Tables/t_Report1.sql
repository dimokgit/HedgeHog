CREATE TABLE [dbo].[t_Report1] (
    [Time]      DATETIMEOFFSET (7) NOT NULL,
    [EventType] CHAR (1)           CONSTRAINT [DF_t_Report1_EventType] DEFAULT ('R') NOT NULL,
    CONSTRAINT [PK_t_Report1] PRIMARY KEY CLUSTERED ([Time] ASC)
);

