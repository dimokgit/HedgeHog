CREATE TABLE [dbo].[RateCode] (
    [Id]      INT          IDENTITY (1, 1) NOT NULL,
    [Name]    VARCHAR (50) NOT NULL,
    [Rate]    FLOAT (53)   NOT NULL,
    [TypeId]  INT          NOT NULL,
    [LayerId] INT          NOT NULL,
    CONSTRAINT [PK_RateCode] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_RateCode_RateCodeLayer] FOREIGN KEY ([LayerId]) REFERENCES [dbo].[RateCodeLayer] ([Id]) ON DELETE NO ACTION ON UPDATE CASCADE,
    CONSTRAINT [FK_RateCode_RateCodeType] FOREIGN KEY ([TypeId]) REFERENCES [dbo].[RateCodeType] ([Id]) ON DELETE CASCADE ON UPDATE CASCADE
);








GO
CREATE UNIQUE NONCLUSTERED INDEX [IX_RateCode_Type_Layer]
    ON [dbo].[RateCode]([TypeId] ASC, [LayerId] ASC);

