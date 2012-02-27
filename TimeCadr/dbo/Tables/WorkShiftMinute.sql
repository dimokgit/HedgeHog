CREATE TABLE [dbo].[WorkShiftMinute] (
    [WorkShiftStart] DATETIMEOFFSET (7) NOT NULL,
    [Minute]         INT                NOT NULL,
    [Hour]           INT                NOT NULL,
    CONSTRAINT [PK_WorkShiftMinute] PRIMARY KEY CLUSTERED ([WorkShiftStart] ASC, [Hour] ASC, [Minute] ASC),
    CONSTRAINT [FK_WorkShiftMinute_WorkShift] FOREIGN KEY ([WorkShiftStart]) REFERENCES [dbo].[WorkShift] ([Start]) ON DELETE CASCADE ON UPDATE NO ACTION
);

