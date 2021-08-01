CREATE VIEW [dbo].[v_TradesBySession] AS
SELECT [Id]
      ,[Buy]
      ,[PL]
      ,[GrossPL]
      ,[Lot]
      ,T.[Pair]
      ,[TimeOpen]
      ,[TimeClose]
      ,[AccountId]
      ,[Commission]
      ,[IsVirtual]
      ,T.[TimeStamp]
      ,[CorridorHeightInPips]
      ,[CorridorMinutesBack]
      ,T.[SessionId]
      ,[PriceOpen]
      ,[PriceClose]
      ,[SessionInfo]
      ,[RunningBalance]
      ,[RunningBalanceTotal]
      ,S.SessionPosition
      ,S.TimeStamp SessionTimeStamp
  FROM [t_Trade] T
  INNER JOIN 
  (
  SELECT 
SessionId
, MAX(Pair) Pair
, MAX(TimeStamp)TimeStamp
, ROW_NUMBER() OVER(PARTITION BY MAX(Pair) ORDER BY MAX(TimeStamp) DESC) SessionPosition
  FROM [FOREX].[dbo].[t_Trade]
  WHERE IsVirtual = 1
  GROUP BY SessionId
  )S ON T.SessionId = S.SessionId AND T.Pair = S.Pair
  WHERE IsVirtual = 1