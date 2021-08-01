CREATE TABLE [dbo].[SP500] (
    [Symbol]      VARCHAR (5) NOT NULL,
    [LoadRates]   BIT         CONSTRAINT [DF_SP500_LoadRates] DEFAULT ((0)) NOT NULL,
    [IsConsensus] BIT         CONSTRAINT [DF_SP500_IsConsensus] DEFAULT ((0)) NOT NULL,
    CONSTRAINT [PK_SP500] PRIMARY KEY CLUSTERED ([Symbol] ASC)
);

