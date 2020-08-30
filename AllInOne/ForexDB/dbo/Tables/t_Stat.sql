CREATE TABLE [dbo].[t_Stat] (
    [Id]     INT            IDENTITY (1, 1) NOT NULL,
    [Time]   DATETIME       NOT NULL,
    [Price]  FLOAT (53)     NOT NULL,
    [Value1] FLOAT (53)     NOT NULL,
    [Value2] FLOAT (53)     NOT NULL,
    [Value3] FLOAT (53)     NOT NULL,
    [Name]   NVARCHAR (MAX) NOT NULL,
    CONSTRAINT [PK_t_Stat] PRIMARY KEY CLUSTERED ([Id] ASC)
);

