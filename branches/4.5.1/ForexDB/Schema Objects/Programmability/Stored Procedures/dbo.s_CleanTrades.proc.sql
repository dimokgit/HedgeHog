CREATE PROC s_CleanTrades AS
DELETE FROM t_Trade
WHERE SessionId IN(
SELECT [SessionId]
  FROM [Forex].[dbo].[v_TradeSession_10]
  WHERE DATEDIFF(dd,[TimeStamp],getdate())>7 AND (GrossPL<0 OR Days<30*6)
  )