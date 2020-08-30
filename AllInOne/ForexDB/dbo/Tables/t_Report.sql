CREATE TABLE [dbo].[t_Report] (
    [Time]      DATETIMEOFFSET (7) NOT NULL,
    [EventType] CHAR (1)           CONSTRAINT [DF_t_Report_EventType] DEFAULT ('R') NOT NULL,
    [TimeLocal] AS                 (CONVERT([datetime],[time],0)) PERSISTED,
    CONSTRAINT [PK_t_Report] PRIMARY KEY CLUSTERED ([Time] ASC)
);

