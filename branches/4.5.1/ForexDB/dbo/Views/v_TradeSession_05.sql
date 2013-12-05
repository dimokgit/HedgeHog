
CREATE VIEW [dbo].[v_TradeSession_05]
AS
SELECT        SUM(PL) AS PL, SUM(GrossPL) AS GrossPL, SUM(Lot) AS Lot, Pair, TimeOpen, MAX(TimeClose) AS TimeClose, MAX(TimeStamp) AS TimeStamp, SessionId, 
                         MAX(SessionInfo) AS SessionInfo,
                         MAX(SessionInfo2) AS SessionInfo2
                         , MIN(RunningBalance) AS RunningBalance
FROM            dbo.t_Trade WITH (nolock)
WHERE        (IsVirtual = 1)
GROUP BY Pair, TimeOpen, SessionId, Buy