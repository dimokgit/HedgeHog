CREATE TABLE [dbo].[WorkDay] (
    [Time]         DATETIMEOFFSET (7) NOT NULL,
    [WorkMinutes]  INT                NULL,
    [LunchMinutes] INT                NULL,
    CONSTRAINT [PK_WorkDay] PRIMARY KEY CLUSTERED ([Time] ASC)
);



