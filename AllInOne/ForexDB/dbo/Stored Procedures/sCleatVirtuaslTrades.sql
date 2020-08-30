CREATE PROCEDURE [dbo].[sCleatVirtuaslTrades] AS
;WITH S AS
(
SELECT 
        SessionId
FROM            dbo.v_TradeSession_10 WITH (nolock)
WHERE        Days <15
)
DELETE t_Trade
WHERE SessionId IN (SELECT SessionId FROM S)
AND (IsVirtual = 1)

