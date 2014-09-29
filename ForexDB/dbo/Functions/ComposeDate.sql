CREATE FUNCTION ComposeDate(@year INT, @month INT, @day INT) 
-- I deliberately do not check if the parameters are valid 
RETURNS TABLE AS RETURN( 
  SELECT DATEADD(DAY, @day - 1, DATEADD(MONTH, @month - 1,  DATEADD(YEAR, @year - 1901, '19010101')))
    AS ComposedDate 
)