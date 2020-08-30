CREATE TYPE [dbo].[dt_VoltsTable] AS TABLE (
    [StartDate] DATETIME   NOT NULL,
    [Volts]     FLOAT (53) NOT NULL,
    [Price]     FLOAT (53) NOT NULL,
    PRIMARY KEY CLUSTERED ([StartDate] ASC));

