CREATE FUNCTION [dbo].[fGetSessionValue](
  @Text varchar(max),
  @Name varchar(128)
)RETURNS TABLE AS 
RETURN(
SELECT /*S1.Value,S2.Value1,*/S2.Value2 Value FROM clrSplit(@Text, ',') S1
OUTER APPLY clrSplitTwo(S1.Value,CHAR(9)) S2
WHERE S2.Value1 = @Name

--SELECT REPLACE(PARSENAME(Split,1),';','.')Value
--FROM
--(
--SELECT REPLACE(REPLACE(S1.Value,'.',';'),':','.') Split FROM clrSplit(@Text,',') S1
--)T WHERE PARSENAME(Split,2) = @Name
)