CREATE TABLE [dbo].[RateCodeByRange] (
    [Id]          INT IDENTITY (1, 1) NOT NULL,
    [TimeStart]   INT NULL,
    [GracePeriod] INT NULL,
    [HourStart]   INT NOT NULL,
    [HourStop]    INT NOT NULL,
    [RateCodeId]  INT NOT NULL,
    CONSTRAINT [PK_RateCodeByRange] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_RateCodeByRange_RateCode] FOREIGN KEY ([RateCodeId]) REFERENCES [dbo].[RateCode] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION
);




GO
CREATE UNIQUE NONCLUSTERED INDEX [IX_RateCodeByRange]
    ON [dbo].[RateCodeByRange]([HourStart] ASC, [HourStop] ASC, [RateCodeId] ASC);

