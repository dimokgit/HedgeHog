CREATE TABLE [dbo].[t_Tick_Volts] (
    [StartDate] DATETIME   NOT NULL,
    [Volts]     FLOAT (53) NULL,
    [Price]     FLOAT (53) NULL,
    CONSTRAINT [PK_t_Tick_Volts] PRIMARY KEY CLUSTERED ([StartDate] ASC)
);

