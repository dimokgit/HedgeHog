CREATE PROCEDURE [dbo].[sCleatVirtuaslTradesBySuperSession] AS
;WITH S AS
(
SELECT 
        SessionId
FROM            dbo.v_TradeSession
WHERE        SuperSessionUID = '46A1E78F-4B46-4E06-9381-FACE45B5432D'
)
DELETE t_Trade
WHERE SessionId IN (SELECT SessionId FROM S)
AND (IsVirtual = 1)