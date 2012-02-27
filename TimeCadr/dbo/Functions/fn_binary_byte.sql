CREATE function [dbo].[fn_binary_byte](
	@Byte tinyint
)RETURNS char(8)
AS
BEGIN
DECLARE @Text varchar(512) SET @Text = ''
DECLARE @i bigint SET @i = 7
WHILE @i >= 0 BEGIN
	SET @Text = ISNULL(@Text,'') + char(48+CONVERT(bit,(@Byte & POWER(2,@i))))
	SET @i = @i -1
END
RETURN @Text
END