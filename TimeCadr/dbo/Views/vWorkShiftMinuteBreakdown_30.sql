CREATE VIEW dbo.vWorkShiftMinuteBreakdown_30
AS
SELECT        WSMB.WorkShiftStart, WSMB.PunchPairStart, WSMB.MinuteDate, WSMB.MinuteDateTime, WSMB.WorkShiftMinute, WSMB.WorkShiftHour, 
                         WSMB.WorkShiftMinuteByHour, WSMB.WorkDayMinute, (WSMB.WorkDayMinute - 1) / 60 + 1 AS WorkDayHour, (WSMB.WorkDayMinute - 1) 
                         % 60 + 1 AS WorkDayMinuteByHour, ISNULL(RCBR.RateCodeId, - 1) AS WSRCId, ISNULL(RCBR.RateCodeTypePriority, 0) AS WSRCTypePriority, 
                         ISNULL(RCBR.RateCodeLayerPriority, 0) AS WSRCLayerPriority
FROM            dbo.vWorkShiftMinuteBreakdown_20 AS WSMB LEFT OUTER JOIN
                         dbo.vRateCodeByRange AS RCBR ON WSMB.WorkShiftHour >= RCBR.HourStart AND WSMB.WorkShiftHour <= RCBR.HourStop
WHERE        (RCBR.RateCodeTypeId = 1)
GO
EXECUTE sp_addextendedproperty @name = N'MS_DiagramPaneCount', @value = 1, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'vWorkShiftMinuteBreakdown_30';


GO
EXECUTE sp_addextendedproperty @name = N'MS_DiagramPane1', @value = N'[0E232FF0-B466-11cf-A24F-00AA00A3EFFF, 1.00]
Begin DesignProperties = 
   Begin PaneConfigurations = 
      Begin PaneConfiguration = 0
         NumPanes = 4
         Configuration = "(H (1[28] 4[32] 2[20] 3) )"
      End
      Begin PaneConfiguration = 1
         NumPanes = 3
         Configuration = "(H (1[50] 4[25] 3) )"
      End
      Begin PaneConfiguration = 2
         NumPanes = 3
         Configuration = "(H (1[50] 2[25] 3) )"
      End
      Begin PaneConfiguration = 3
         NumPanes = 3
         Configuration = "(H (4 [30] 2 [40] 3))"
      End
      Begin PaneConfiguration = 4
         NumPanes = 2
         Configuration = "(H (1[18] 3) )"
      End
      Begin PaneConfiguration = 5
         NumPanes = 2
         Configuration = "(H (2[66] 3) )"
      End
      Begin PaneConfiguration = 6
         NumPanes = 2
         Configuration = "(H (4 [50] 3))"
      End
      Begin PaneConfiguration = 7
         NumPanes = 1
         Configuration = "(V (3) )"
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
         Begin Table = "WSMB"
            Begin Extent = 
               Top = 6
               Left = 38
               Bottom = 206
               Right = 257
            End
            DisplayFlags = 280
            TopColumn = 0
         End
         Begin Table = "RCBR"
            Begin Extent = 
               Top = 6
               Left = 295
               Bottom = 280
               Right = 496
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
      Begin ColumnWidths = 13
         Width = 284
         Width = 1500
         Width = 1500
         Width = 1500
         Width = 2865
         Width = 1500
         Width = 1500
         Width = 2145
         Width = 1890
         Width = 2310
         Width = 2280
         Width = 1890
         Width = 1500
      End
   End
   Begin CriteriaPane = 
      Begin ColumnWidths = 11
         Column = 3795
         Alias = 3735
         Table = 2805
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
End', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'vWorkShiftMinuteBreakdown_30';



