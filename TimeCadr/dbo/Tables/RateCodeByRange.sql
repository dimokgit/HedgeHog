CREATE TABLE [dbo].[RateCodeByRange] (
    [Id]            INT IDENTITY (1, 1) NOT NULL,
    [TimeStart]     INT NULL,
    [TimeStart_]    AS  (timefromparts([TimeStart],(0),(0),(0),(0))) PERSISTED,
    [TimeStop]      AS  (timefromparts(([TimeStart]+[HourStop])%(24),(0),(0),(0),(0))) PERSISTED,
    [IsTimeBetween] AS  (case when ([TimeStart]+[HourStop])%(24)>[TimeStart] then (1) else (0) end) PERSISTED NOT NULL,
    [GracePeriod]   INT NULL,
    [HourStart]     INT NOT NULL,
    [HourStop]      INT NOT NULL,
    [RateCodeId]    INT NOT NULL,
    CONSTRAINT [PK_RateCodeByRange] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_RateCodeByRange_RateCode] FOREIGN KEY ([RateCodeId]) REFERENCES [dbo].[RateCode] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION
);






GO
CREATE UNIQUE NONCLUSTERED INDEX [IX_RateCodeByRange]
    ON [dbo].[RateCodeByRange]([HourStart] ASC, [HourStop] ASC, [RateCodeId] ASC);

