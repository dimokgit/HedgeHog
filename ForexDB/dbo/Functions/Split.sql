CREATE Function dbo.Split
(          
      @String VARCHAR(MAX),  -- Variable for string
      @delimiter VARCHAR(50) -- Delimiter in the string
)
RETURNS @Table TABLE(        --Return type of the function
Split VARCHAR(MAX)
)
BEGIN
     Declare @Xml AS XML 
-- Replace the delimiter to the opeing and closing tag
--to make it an xml document
     SET @Xml = cast(('<A>'+replace(@String,@delimiter,'</A><A>')+'</A>') AS XML) 
--Query this xml document via xquery to split rows
--and insert it into table to return.
     INSERT INTO @Table SELECT A.value('.', 'varchar(max)') as [Column] FROM @Xml.nodes('A') AS FN(A) 
RETURN
END