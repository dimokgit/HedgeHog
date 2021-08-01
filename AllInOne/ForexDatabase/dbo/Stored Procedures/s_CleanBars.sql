CREATE PROCEDURE [dbo].[s_CleanBars] AS
DELETE FROM [dbo].[t_Bar]
      WHERE pair = 'usd/jpy' and StartDate>'1/16/2016'

