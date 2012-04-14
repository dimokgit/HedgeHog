CREATE VIEW dbo.vRateCode
AS
SELECT        TOP (10000) RC.Id, RC.Name, RC.Rate, RCT.Name AS Type, RC.TypeId, RCL.Name AS Layer, RC.LayerId, RCR.Name AS [Rule], RCT.RuleId, 
                         RCT.Priority AS RateCodeTypePriority, RCL.Priority AS RateCodeLayerPriority, RCR.Id AS RulePriority, RCT.IsRuleOver, RCT.IsRuleExtra
FROM            dbo.RateCode AS RC INNER JOIN
                         dbo.RateCodeType AS RCT ON RC.TypeId = RCT.Id INNER JOIN
                         dbo.RateCodeLayer AS RCL ON RC.LayerId = RCL.Id INNER JOIN
                         dbo.RateCodeRule AS RCR ON RCT.RuleId = RCR.Id
ORDER BY RulePriority, RateCodeLayerPriority, RateCodeTypePriority, RC.Name
GO
EXECUTE sp_addextendedproperty @name = N'MS_DiagramPaneCount', @value = 2, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'vRateCode';




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
         Begin Table = "RC"
            Begin Extent = 
               Top = 6
               Left = 38
               Bottom = 177
               Right = 208
            End
            DisplayFlags = 280
            TopColumn = 0
         End
         Begin Table = "RCT"
            Begin Extent = 
               Top = 6
               Left = 246
               Bottom = 173
               Right = 416
            End
            DisplayFlags = 280
            TopColumn = 2
         End
         Begin Table = "RCL"
            Begin Extent = 
               Top = 188
               Left = 267
               Bottom = 315
               Right = 437
            End
            DisplayFlags = 280
            TopColumn = 0
         End
         Begin Table = "RCR"
            Begin Extent = 
               Top = 6
               Left = 454
               Bottom = 118
               Right = 624
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
         Width = 1920
         Width = 2220
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
         Alias = 1950
         Table = 1410
         Output = 720
         Append = 1400
         NewValue = 1170
         SortType = 1350
         SortOrder = 1410', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'vRateCode';












GO
EXECUTE sp_addextendedproperty @name = N'MS_DiagramPane2', @value = N'GroupBy = 1350
         Filter = 1350
         Or = 1350
         Or = 1350
         Or = 1350
      End
   End
End', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'vRateCode';



