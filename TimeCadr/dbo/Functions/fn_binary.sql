CREATE FUNCTION [dbo].[fn_binary](
	@binvalue varbinary(255),
	@reverse bit = 0
)RETURNS varchar(8000)
as BEGIN
   declare @i int
   declare @length int
   declare @hexstring char(16)
DECLARE @CharValue varchar(8000) SET @CharValue = ''

   select @i = 1
   select @length = datalength(@binvalue)

   while (@i <= @length)
   begin

     declare @tempint int
     declare @firstint int
     declare @secondint int

     select @tempint = convert(int, substring(@binvalue,@i,1))

		 if @reverse = 0 select @charvalue = @charvalue + dbo.fn_binary_byte(@tempint)
		 else select @charvalue = dbo.fn_binary_byte(@tempint) + @charvalue

     select @i = @i + 1

   end
RETURN @CharValue
END