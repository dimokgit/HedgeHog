CREATE TABLE [dbo].[PunchDirectionSalutation] (
    [PunchDirectionId] INT          NOT NULL,
    [PunchTypeId]      INT          NOT NULL,
    [Salutation]       VARCHAR (10) NULL,
    CONSTRAINT [PK_PunchDirectionSalutation] PRIMARY KEY CLUSTERED ([PunchDirectionId] ASC, [PunchTypeId] ASC),
    CONSTRAINT [FK_PunchDirectionSalutation_PunchDirection] FOREIGN KEY ([PunchDirectionId]) REFERENCES [dbo].[PunchDirection] ([Id]) ON DELETE NO ACTION ON UPDATE CASCADE,
    CONSTRAINT [FK_PunchDirectionSalutation_PunchType] FOREIGN KEY ([PunchTypeId]) REFERENCES [dbo].[PunchType] ([Id]) ON DELETE NO ACTION ON UPDATE CASCADE
);

