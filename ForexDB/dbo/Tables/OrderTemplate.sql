CREATE TABLE [dbo].[OrderTemplate] (
    [ID]          INT          NOT NULL,
    [Stop]        INT          DEFAULT ((0)) NOT NULL,
    [Limit]       INT          DEFAULT ((0)) NOT NULL,
    [Price]       FLOAT (53)   DEFAULT ((0)) NOT NULL,
    [Lot]         INT          DEFAULT ((0)) NOT NULL,
    [StopOrderID] INT          DEFAULT ((0)) NOT NULL,
    [Pair]        NVARCHAR (7) NULL,
    CONSTRAINT [PK_OrderTempate] PRIMARY KEY CLUSTERED ([ID] ASC)
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UQ__OrderTempate__00000000000005FF]
    ON [dbo].[OrderTemplate]([ID] ASC);

