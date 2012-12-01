﻿CREATE FUNCTION [dbo].[fn_ColumnUpdated](
	@ColBinary varbinary(8000),
	@TableName sysname
)RETURNS @T TABLE(Name sysname) AS 
BEGIN

IF ISNUMERIC(@TableName) = 1 SET @TableName = dbo.fn_TriggerTable(OBJECT_NAME(@TableName))

DECLARE @Schema sysname
SET @Schema = ISNULL(parsename(@TableNAme,2),'dbo')
SET @TableName = parsename(@TableNAme,1)

DECLARE @Cols varchar(255) SET @Cols = dbo.fn_binary(@ColBinary,1)

DECLARE @ColNames nvarchar(4000),@i int SET @i = 1
WHILE @Cols > '' BEGIN
  INSERT INTO @T
	SELECT COLUMN_NAME
	FROM INFORMATION_SCHEMA.COLUMNS
	WHERE TABLE_NAME = @TableName AND
				TABLE_SCHEMA = @Schema AND
				COLUMNPROPERTY(OBJECT_ID(@Schema + '.' + TABLE_NAME),COLUMN_NAME, 'ColumnID') = @i AND
				RIGHT(@Cols,1) = '1'

	SELECT @Cols = LEFT(@Cols,LEN(@Cols)-1), @i = @i+1
END

RETURN

END