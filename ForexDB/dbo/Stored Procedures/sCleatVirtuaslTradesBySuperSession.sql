CREATE PROCEDURE [dbo].[sCleatVirtuaslTradesBySuperSession] 
@SuperSessionUID uniqueidentifier
AS
;WITH S AS
(
SELECT 
        SessionId
FROM            dbo.v_TradeSession
WHERE        SuperSessionUID = @SuperSessionUID
)
DELETE t_Trade
OUTPUT deleted.*
WHERE SessionId IN (SELECT SessionId FROM S)
AND (IsVirtual = 1)