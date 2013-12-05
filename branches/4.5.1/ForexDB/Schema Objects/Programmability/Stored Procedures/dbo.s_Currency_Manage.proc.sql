CREATE PROCEDURE s_Currency_Manage AS
--INSERT INTO t_Pair
--SELECT 'EUR'UNION 
--SELECT 'USD'UNION 
--SELECT 'GBP'UNION 
--SELECT 'JPY'UNION 
--SELECT 'CHF'UNION 
--SELECT 'CAD'

SELECT p1.NAme+'/'+p2.Name
FROM t_Currency p1 CROSS JOIN t_Currency p2
WHERE p1.Weight < p2.Weight
ORDER BY p1.Weight,p2.Weight