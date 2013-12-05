CREATE TABLE [stats].[MonthlyStats] (
    [Month]      INT          NOT NULL,
    [Pair]       VARCHAR (10) CONSTRAINT [DF_MonthlyStats_Pair] DEFAULT ('EUR/JPY') NOT NULL,
    [Period]     INT          CONSTRAINT [DF_MonthlyStats_Period] DEFAULT ((1)) NOT NULL,
    [StDevAvg]   FLOAT (53)   NOT NULL,
    [StDevStDev] FLOAT (53)   NOT NULL,
    [Count]      INT          NOT NULL,
    [Date]       DATE         NOT NULL,
    CONSTRAINT [PK_MonthlyStats] PRIMARY KEY CLUSTERED ([Pair] ASC, [Period] ASC, [Date] ASC)
);

