CREATE TABLE [dbo].[Blackout] (
    [ID]       INT      IDENTITY (1, 1) NOT NULL,
    [TimeFrom] DATETIME NOT NULL,
    [TimeTo]   DATETIME NOT NULL,
    CONSTRAINT [PK_Blackout] PRIMARY KEY CLUSTERED ([ID] ASC)
);

