CREATE TABLE [dbo].[SalaryBreakdown] (
    [RateCode]  VARCHAR (50)    NOT NULL,
    [Salary]    NUMERIC (38, 2) NOT NULL,
    [DateStamp] DATETIME        CONSTRAINT [DF_SalaryBreakdown_DateStamp] DEFAULT (getdate()) NOT NULL,
    CONSTRAINT [PK_SalaryBreakdown] PRIMARY KEY CLUSTERED ([RateCode] ASC)
);


GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'SalaryBreakdown';

