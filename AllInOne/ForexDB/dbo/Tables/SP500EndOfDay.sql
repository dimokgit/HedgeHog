CREATE TABLE [dbo].[SP500EndOfDay] (
    [Pair]  NCHAR (10) NOT NULL,
    [Date]  DATE       NOT NULL,
    [Price] REAL       NULL,
    CONSTRAINT [PK_SP500EndOfDay] PRIMARY KEY CLUSTERED ([Pair] ASC, [Date] ASC)
);

