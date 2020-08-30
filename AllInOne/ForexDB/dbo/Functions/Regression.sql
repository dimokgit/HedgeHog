--ALTER TYPE [dbo].[dt_VoltsTable] AS TABLE(
--	[StartDate] [datetime] NOT NULL,
--	[Volts] [float] NOT NULL,
--	[Price] [float] NOT NULL,
--	PRIMARY KEY CLUSTERED 
--(
--	[StartDate] ASC
--)WITH (IGNORE_DUP_KEY = OFF)
--)
--GO


CREATE FUNCTION [dbo].[Regression](
@Table [dt_VoltsTable] READONLY
)RETURNS float AS
BEGIN

RETURN(
SELECT TOP 1 Volts FROM @Table
)
END