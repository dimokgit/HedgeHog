CREATE FUNCTION [dbo].[fGetSessionValue](
  @Text varchar(max),
  @Name varchar(128)
)RETURNS TABLE AS 
RETURN(
SELECT REPLACE(PARSENAME(Split,1),';','.')Value
FROM
(
SELECT REPLACE(REPLACE(S1.Value,'.',';'),':','.') Split FROM clrSplit(@Text,',') S1
)T WHERE PARSENAME(Split,2) = @Name
)