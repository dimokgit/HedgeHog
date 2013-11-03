



CREATE VIEW [dbo].[v_TradeSession] AS
SELECT        TS.Pair, TS.SessionId, S.SuperUid AS SuperSessionUid, TS.TimeStamp, TS.Count, TS.GrossPL, TS.Days,S.Profitability, ISNULL(S.MaximumLot, TS.Lot * 1.4) AS Lot, 
                         TS.LotA / O.BaseUnitSize AS LotA, TS.LotSD / O.BaseUnitSize AS LotSD, TS.DollarsPerMonth, TS.PL, TS.MinutesInTest, TS.DaysPerMinute, ISNULL(S.Profitability, 
                         TS.DollarsPerMonth) / TS.Lot * O.BaseUnitSize AS DollarPerLot, TS.SessionInfo,
                         PriceCmaLevels.Value AS PriceCmaLevels, 
                         CAST(CorridorDistanceRatio.Value AS float) AS CorridorDistanceRatio,
                         PLToCorridorExitRatio.Value AS PLToCorridorExitRatio,
                         ProfitToLossExitRatio.Value AS ProfitToLossExitRatio, 
                         BarsCount.Value AS BarsCount,
                         ISNULL(S.MinimumGross, TS.MinimumGross * 1.4) AS MinimumGross,
                         TS.DateStart, TS.DateStop, 
                         ISNULL(S.Profitability, TS.DollarsPerMonth) / NULLIF (- S.MinimumGross, 0) AS LossToProfit,
                         WaveStDevRatio.Value  AS WaveStDevRatio, 
                         DistanceIterations.Value AS DistanceIterations, 
                         CorrelationMinimum.Value AS CorrelationMinimum, 
                         ScanCorridorBy.Value AS ScanCorridorBy,
                         TPLR.Value AS TakeProfitLimitRatio,
                         TradingAngleRange.Value TradingAngleRange,
                         PolyOrder.Value PolyOrder,
                         MovingAverageType.Value MovingAverageType,
                         PriceCmaLevels_.Value PriceCmaLevels_,
                         TestFileName.Value TestFileName
FROM            dbo.v_TradeSession_10 AS TS 
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'TakeProfitLimitRatio')TPLR
INNER JOIN dbo.t_Offer AS O ON TS.Pair = O.Pair
LEFT OUTER JOIN dbo.t_Session AS S WITH (nolock) ON TS.SessionId = S.Uid
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'WaveStDevRatio')  AS WaveStDevRatio
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'DistanceIterations') AS DistanceIterations
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'CorrelationMinimum') AS CorrelationMinimum 
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'ScanCorridorBy') AS ScanCorridorBy
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'PriceCmaLevels_') AS PriceCmaLevels
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'CorridorDistanceRatio')CorridorDistanceRatio
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'PLToCorridorExitRatio') AS PLToCorridorExitRatio
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'ProfitToLossExitRatio_') AS ProfitToLossExitRatio
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'BarsCount') AS BarsCount
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'TradingAngleRange_') AS TradingAngleRange
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'PolyOrder') AS PolyOrder
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'TestFileName') AS TestFileName
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'MovingAverageType') AS MovingAverageType
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'PriceCmaLevels_') AS PriceCmaLevels_

GO
EXECUTE sp_addextendedproperty @name = N'MS_DiagramPane1', @value = N'[0E232FF0-B466-11cf-A24F-00AA00A3EFFF, 1.00]
Begin DesignProperties = 
   Begin PaneConfigurations = 
      Begin PaneConfiguration = 0
         NumPanes = 4
         Configuration = "(H (1[40] 4[20] 2[20] 3) )"
      End
      Begin PaneConfiguration = 1
         NumPanes = 3
         Configuration = "(H (1 [50] 4 [25] 3))"
      End
      Begin PaneConfiguration = 2
         NumPanes = 3
         Configuration = "(H (1 [50] 2 [25] 3))"
      End
      Begin PaneConfiguration = 3
         NumPanes = 3
         Configuration = "(H (4 [30] 2 [40] 3))"
      End
      Begin PaneConfiguration = 4
         NumPanes = 2
         Configuration = "(H (1 [56] 3))"
      End
      Begin PaneConfiguration = 5
         NumPanes = 2
         Configuration = "(H (2 [66] 3))"
      End
      Begin PaneConfiguration = 6
         NumPanes = 2
         Configuration = "(H (4 [50] 3))"
      End
      Begin PaneConfiguration = 7
         NumPanes = 1
         Configuration = "(V (3))"
      End
      Begin PaneConfiguration = 8
         NumPanes = 3
         Configuration = "(H (1[56] 4[18] 2) )"
      End
      Begin PaneConfiguration = 9
         NumPanes = 2
         Configuration = "(H (1 [75] 4))"
      End
      Begin PaneConfiguration = 10
         NumPanes = 2
         Configuration = "(H (1[66] 2) )"
      End
      Begin PaneConfiguration = 11
         NumPanes = 2
         Configuration = "(H (4 [60] 2))"
      End
      Begin PaneConfiguration = 12
         NumPanes = 1
         Configuration = "(H (1) )"
      End
      Begin PaneConfiguration = 13
         NumPanes = 1
         Configuration = "(V (4))"
      End
      Begin PaneConfiguration = 14
         NumPanes = 1
         Configuration = "(V (2))"
      End
      ActivePaneConfig = 0
   End
   Begin DiagramPane = 
      Begin Origin = 
         Top = 0
         Left = 0
      End
      Begin Tables = 
         Begin Table = "v_TradeSession_10"
            Begin Extent = 
               Top = 6
               Left = 38
               Bottom = 125
               Right = 205
            End
            DisplayFlags = 280
            TopColumn = 0
         End
      End
   End
   Begin SQLPane = 
   End
   Begin DataPane = 
      Begin ParameterDefaults = ""
      End
      Begin ColumnWidths = 15
         Width = 284
         Width = 1500
         Width = 1500
         Width = 1500
         Width = 1500
         Width = 1500
         Width = 1500
         Width = 1500
         Width = 1500
         Width = 1500
         Width = 1500
         Width = 1500
         Width = 1500
         Width = 1500
         Width = 1500
      End
   End
   Begin CriteriaPane = 
      Begin ColumnWidths = 11
         Column = 1440
         Alias = 900
         Table = 1170
         Output = 720
         Append = 1400
         NewValue = 1170
         SortType = 1350
         SortOrder = 1410
         GroupBy = 1350
         Filter = 1350
         Or = 1350
         Or = 1350
         Or = 1350
      End
   End
End', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'v_TradeSession';




GO
EXECUTE sp_addextendedproperty @name = N'MS_DiagramPaneCount', @value = 1, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'v_TradeSession';

