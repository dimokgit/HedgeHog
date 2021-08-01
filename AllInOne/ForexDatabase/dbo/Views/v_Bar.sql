CREATE VIEW [dbo].[v_Bar] AS
SELECT B.Row, B.Pair, B.Period, B.StartDate, B.AskHigh, B.AskLow, B.AskOpen, B.AskClose, B.BidHigh, B.BidLow, B.BidOpen, B.BidClose, B.Volume, B.ID, B.StartDateLocal
, (B.AskHigh + B.AskLow + B.BidHigh + B.BidLow) / 4 AS Price
, (B.AskHigh - B.BidHigh + B.AskLow - B.BidLow) / 2 / O.PipSize AS PriceHeightInPips
                         ,O.PipSize
FROM            t_Bar AS B INNER JOIN
                         t_Offer AS O ON B.Pair = O.Pair