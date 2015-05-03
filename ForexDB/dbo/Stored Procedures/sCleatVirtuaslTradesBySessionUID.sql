CREATE PROCEDURE [dbo].[sCleatVirtuaslTradesBySessionUID] 
@SessionUID uniqueidentifier
AS
DELETE t_Trade
OUTPUT deleted.*
WHERE SessionId = @SessionUID
AND (IsVirtual = 1)