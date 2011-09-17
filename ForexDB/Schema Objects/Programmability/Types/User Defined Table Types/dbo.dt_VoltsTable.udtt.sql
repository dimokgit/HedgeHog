CREATE TYPE [dbo].[dt_VoltsTable] AS  TABLE (
    [StartDate] DATETIME NOT NULL,
    [Volts]     FLOAT    NOT NULL,
    [Price]     FLOAT    NOT NULL,
    PRIMARY KEY CLUSTERED ([StartDate] ASC));

