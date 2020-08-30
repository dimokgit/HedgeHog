CREATE VIEW [dbo].[v_TradeSession_10]AS
SELECT        TOP (100000) Pair, SessionId, MAX(TimeStamp) AS TimeStamp, COUNT(*) AS Count, SUM(GrossPL) AS GrossPL, DATEDIFF(dd, MIN(TimeOpen), MAX(TimeClose)) 
                         AS Days, MAX(Lot) AS Lot, AVG(Lot) AS LotA, STDEV(Lot) AS LotSD, SUM(GrossPL) / NULLIF (DATEDIFF(hh, MIN(TimeOpen), MAX(TimeClose)), 0) 
                         * 30.5 * 24 AS DollarsPerMonth, CONVERT(numeric(10, 2), SUM(PL * Lot) / SUM(Lot)) AS PL, DATEDIFF(n, MIN(TimeStamp), MAX(TimeStamp)) AS MinutesInTest, 
                         CONVERT(float, DATEDIFF(dd, MIN(TimeOpen), MAX(TimeClose))) / NULLIF (DATEDIFF(n, MIN(TimeStamp), MAX(TimeStamp)), 0.0) AS DaysPerMinute, 
                         MAX(SessionInfo) AS SessionInfo,
                         MAX(SessionInfo2) AS SessionInfo2,
                          MIN(RunningBalance) AS MinimumGross, CAST(MIN(TimeOpen) AS date) AS DateStart, CAST(MAX(TimeClose) AS date)  AS DateStop,
                          SUM(CASE WHEN PL >= 0 THEN 1 ELSE 0 END)ProfitCount,
                          SUM(CASE WHEN PL < 0 THEN 1 ELSE 0 END)LossCount
FROM            dbo.v_TradeSession_05 AS T
GROUP BY SessionId, Pair
ORDER BY TimeStamp DESC

GO
EXECUTE sp_addextendedproperty @name = N'MS_DiagramPane1', @value = N'[0E232FF0-B466-11cf-A24F-00AA00A3EFFF, 1.00]
Begin DesignProperties = 
   Begin PaneConfigurations = 
      Begin PaneConfiguration = 0
         NumPanes = 4
         Configuration = "(H (1[20] 4[44] 2[10] 3) )"
      End
      Begin PaneConfiguration = 1
         NumPanes = 3
         Configuration = "(H (1[35] 4[39] 3) )"
      End
      Begin PaneConfiguration = 2
         NumPanes = 3
         Configuration = "(H (1 [50] 2 [25] 3))"
      End
      Begin PaneConfiguration = 3
         NumPanes = 3
         Configuration = "(H (4[53] 2[18] 3) )"
      End
      Begin PaneConfiguration = 4
         NumPanes = 2
         Configuration = "(H (1[56] 3) )"
      End
      Begin PaneConfiguration = 5
         NumPanes = 2
         Configuration = "(H (2 [66] 3))"
      End
      Begin PaneConfiguration = 6
         NumPanes = 2
         Configuration = "(H (4[50] 3) )"
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
         Begin Table = "T"
            Begin Extent = 
               Top = 6
               Left = 38
               Bottom = 300
               Right = 278
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
      Begin ColumnWidths = 14
         Width = 284
         Width = 780
         Width = 1995
         Width = 1365
         Width = 630
         Width = 1500
         Width = 555
         Width = 1500
         Width = 1500
         Width = 1500
         Width = 1800
         Width = 1215
         Width = 1500
         Width = 1500
      End
   End
   Begin CriteriaPane = 
      Begin ColumnWidths = 12
         Column = 3255
         Alias = 1950
         Table = 1170
         Output = 720
         Append = 1400
         NewValue = 1170
         SortType = 1350
         SortOrder = 1050
         GroupBy = 1350
         Filter = 1350
         Or = 1350
         Or = 1350
         Or = 1350
      End
   End
End
', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'v_TradeSession_10';


GO
EXECUTE sp_addextendedproperty @name = N'MS_DiagramPaneCount', @value = 1, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'v_TradeSession_10';

