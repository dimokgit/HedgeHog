CREATE FUNCTION [dbo].[fn_ColumnsUpdated](
	@ColBinary varbinary(8000),
	@TableName sysname
)RETURNS nvarchar(4000) AS BEGIN

IF ISNUMERIC(@TableName) = 1 SET @TableName = dbo.fn_TriggerTable(OBJECT_NAME(@TableName))

DECLARE @Schema sysname
SET @Schema = ISNULL(parsename(@TableNAme,2),'dbo')
SET @TableName = parsename(@TableNAme,1)

DECLARE @Cols varchar(255) SET @Cols = dbo.fn_binary(@ColBinary,1)

DECLARE @ColNames nvarchar(4000),@i int SET @i = 1
WHILE @Cols > '' BEGIN
	SELECT @ColNames = ISNULL(@ColNames+',','')+COLUMN_NAME
	FROM INFORMATION_SCHEMA.COLUMNS
	WHERE TABLE_NAME = @TableName AND
				TABLE_SCHEMA = @Schema AND
				COLUMNPROPERTY(OBJECT_ID(@Schema + '.' + TABLE_NAME),COLUMN_NAME, 'ColumnID') = @i AND
				RIGHT(@Cols,1) = '1'

	SELECT @Cols = LEFT(@Cols,LEN(@Cols)-1), @i = @i+1
END

RETURN @ColNames

END