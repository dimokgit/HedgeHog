
CREATE VIEW [dbo].[v_TradeSession] AS
SELECT        TS.Pair, TS.SessionId, S.SuperUid AS SuperSessionUid, TS.TimeStamp, TS.Count, TS.GrossPL, TS.Days,S.Profitability, ISNULL(S.MaximumLot, TS.Lot * 1.4) AS Lot, 
                         TS.LotA / O.BaseUnitSize AS LotA, TS.LotSD / O.BaseUnitSize AS LotSD, TS.DollarsPerMonth, TS.PL, TS.MinutesInTest, TS.DaysPerMinute, ISNULL(S.Profitability, 
                         TS.DollarsPerMonth) / TS.Lot * O.BaseUnitSize AS DollarPerLot, TS.SessionInfo,
                         CAST(CmaPasses.Value AS float) AS CmaPasses,
                         CAST(PriceCmaLevels.Value AS float) AS PriceCmaLevels,
						 
                         ISNULL(S.MinimumGross, TS.MinimumGross * 1.4) AS MinimumGross,
                         TS.DateStart, TS.DateStop, 
                         ISNULL(S.Profitability, TS.DollarsPerMonth) / NULLIF (- S.MinimumGross, 0) AS LossToProfit,
                         TPLR.Value AS TakeProfitLimitRatio,
                         TestFileName.Value TestFileName,
                         ProfitCount,LossCount,
                         CAST(ProfitCount AS float)/NULLIF(Count,0) PLRatio,
						 CAST(RatesDistanceMin.Value AS float) RatesDistanceMin
FROM            dbo.v_TradeSession_10 AS TS 
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'TakeProfitLimitRatio')TPLR
INNER JOIN dbo.t_Offer AS O ON TS.Pair = O.Pair
LEFT OUTER JOIN dbo.t_Session AS S WITH (nolock) ON TS.SessionId = S.Uid
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'CmaPasses')CmaPasses
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'PriceCmaLevels_')PriceCmaLevels
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'TestFileName') AS TestFileName
OUTER APPLY fGetSessionValue(TS.SessionInfo, 'RatesDistanceMin') AS RatesDistanceMin
--where ts.SessionId='700BD5EB-8D20-4EA1-9B97-3A2F2E2206E1'


GO
EXECUTE sp_addextendedproperty @name = N'MS_DiagramPane1', @value = N'[0E232FF0-B466-11cf-A24F-00AA00A3EFFF, 1.00]
Begin DesignProperties = 
   Begin PaneConfigurations = 
      Begin PaneConfiguration = 0
         NumPanes = 4
         Configuration = "(H (1[36] 4[25] 2[24] 3) )"
      End
      Begin PaneConfiguration = 1
         NumPanes = 3
         Configuration = "(H (1[32] 4[44] 3) )"
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
         Configuration = "(H (1[28] 4) )"
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
         Begin Table = "TS"
            Begin Extent = 
               Top = 6
               Left = 38
               Bottom = 386
               Right = 205
            End
            DisplayFlags = 280
            TopColumn = 0
         End
         Begin Table = "O"
            Begin Extent = 
               Top = 0
               Left = 355
               Bottom = 163
               Right = 525
            End
            DisplayFlags = 280
            TopColumn = 0
         End
         Begin Table = "S"
            Begin Extent = 
               Top = 169
               Left = 351
               Bottom = 340
               Right = 522
            End
            DisplayFlags = 280
            TopColumn = 3
         End
      End
   End
   Begin SQLPane = 
   End
   Begin DataPane = 
      Begin ParameterDefaults = ""
      End
      Begin ColumnWidths = 26
         Width = 284
         Width = 1500
         Width = 1500
         Width = 1980
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
         Column = 6420
         Alias = 2445
         Table = 1170
         Output = 720
         Append = 1400
         NewValue = 1170
         SortType = 1350
         SortOrder = 1410
         ', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'v_TradeSession';


GO
EXECUTE sp_addextendedproperty @name = N'MS_DiagramPane2', @value = N'GroupBy = 1350
         Filter = 1350
         Or = 1350
         Or = 1350
         Or = 1350
      End
   End
End
', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'v_TradeSession';


GO
EXECUTE sp_addextendedproperty @name = N'MS_DiagramPaneCount', @value = 2, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'v_TradeSession';

