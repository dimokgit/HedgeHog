CREATE TABLE [dbo].[RateCodeType] (
    [Id]          INT          IDENTITY (1, 1) NOT NULL,
    [Name]        VARCHAR (50) NOT NULL,
    [Priority]    INT          NOT NULL,
    [RuleId]      INT          NOT NULL,
    [IsRuleOver]  AS           (case [RuleId] when (1) then (1) else (0) end) PERSISTED NOT NULL,
    [IsRuleExtra] AS           (case [RuleId] when (2) then (1) else (0) end) PERSISTED NOT NULL,
    CONSTRAINT [PK_RateCodeType] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_RateCodeType_RateCodeRule] FOREIGN KEY ([RuleId]) REFERENCES [dbo].[RateCodeRule] ([Id]) ON DELETE NO ACTION ON UPDATE CASCADE
);







