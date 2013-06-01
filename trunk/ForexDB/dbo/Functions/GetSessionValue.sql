CREATE FUNCTION [dbo].[GetSessionValue](
  @Text varchar(max),
  @Name varchar(128)
)RETURNS varchar(128) AS BEGIN
RETURN(
SELECT REPLACE(PARSENAME(Split,1),';','.')
FROM
(
SELECT REPLACE(REPLACE(S1.Split,'.',';'),':','.') Split FROM Split(@Text,',') S1
)T WHERE PARSENAME(Split,2) = @Name
)
END