CREATE TABLE [dbo].[t_Currency] (
    [Name]    VARCHAR (3) NOT NULL,
    [Weight]  INT         CONSTRAINT [DF_t_Currency_Weight] DEFAULT ((0)) NOT NULL,
    [IsOn]    BIGINT      CONSTRAINT [DF_t_Currency_IsOn] DEFAULT ((1)) NOT NULL,
    [IsPrime] BIT         CONSTRAINT [DF_t_Currency_IsPrime] DEFAULT ((0)) NOT NULL,
    CONSTRAINT [PK_t_Currency] PRIMARY KEY CLUSTERED ([Name] ASC)
);

