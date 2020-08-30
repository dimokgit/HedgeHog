CREATE TABLE [dbo].[t_Volts] (
    [StartDate] DATETIME       NOT NULL,
    [Volts]     NUMERIC (9, 3) NOT NULL,
    [Average]   NUMERIC (9, 5) NULL,
    CONSTRAINT [PK_t_Volts] PRIMARY KEY CLUSTERED ([StartDate] ASC)
);

