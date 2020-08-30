CREATE TABLE [dbo].[t_Offer] (
    [Pair]         VARCHAR (8) NOT NULL,
    [Digits]       INT         NOT NULL,
    [PipCost]      FLOAT (53)  NOT NULL,
    [MMR]          FLOAT (53)  NOT NULL,
    [PipSize]      FLOAT (53)  NOT NULL,
    [BaseUnitSize] INT         CONSTRAINT [DF_t_Offer_BaseUnitSize] DEFAULT ((1000)) NOT NULL,
    CONSTRAINT [PK_t_Offer] PRIMARY KEY CLUSTERED ([Pair] ASC)
);

