CREATE FUNCTION [dbo].[fn_TriggerTable](
	@TriggerName sysname
)RETURNS sysname AS BEGIN
RETURN(
SELECT so1.name FROM sys.sysobjects AS so INNER JOIN sys.sysobjects AS so1 ON so.parent_obj = so1.id WHERE so.name LIKE @TriggerName
)
--DECLARE @TriggerName sysname SET @TriggerName = 'trs_
--SELECT sch.name+'.'+so1.name
--FROM sys.sysobjects AS so
--INNER JOIN sys.sysobjects AS so1 ON so.parent_obj = so1.id
--INNER JOIN sys.schemas sch ON so1.uid = sch.schema_id
--WHERE so.name LIKE @TriggerName

END