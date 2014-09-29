CREATE TABLE [dbo].[t_Stat] (
    [Id]     INT            IDENTITY (1, 1) NOT NULL,
    [Time]   DATETIME       NOT NULL,
    [Price]  FLOAT          NOT NULL,
    [Value1] FLOAT          NOT NULL,
    [Value2] FLOAT          NOT NULL,
    [Value3] FLOAT          NOT NULL,
    [Name]   NVARCHAR (MAX) NOT NULL
);

