CREATE PROCEDURE [dbo].[s_PrepTicks] AS
TRUNCATE TABLE t_Tick_20
INSERT INTO            t_Tick_20
SELECT     StartDate, Diff,AskMax,BidMax,AskMin,BidMin,AskAvg,BidAvg
--INTO t_Tick_20
FROM         v_Tick_20

TRUNCATE TABLE t_Tick_Volts
INSERT INTO t_Tick_Volts
SELECT     StartDate, Volts, Price
--INTO t_Tick_Volts
FROM         v_Tick_Volts

