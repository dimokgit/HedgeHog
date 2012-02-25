CREATE VIEW dbo.vPunch
WITH  VIEW_METADATA
AS
SELECT        dbo.Punch.Id, dbo.Punch.Time, dbo.PunchDirection.Name + '' AS Direction, dbo.Punch.DirectionId, PDS.Salutation + ' ' + dbo.PunchType.Name AS Type, 
                         dbo.Punch.TypeId, dbo.Punch.IsOutOfSequence, ISNULL(ppStart.Start, ppStop.Start) AS PairStart
FROM            dbo.Punch INNER JOIN
                         dbo.PunchDirection ON dbo.Punch.DirectionId = dbo.PunchDirection.Id INNER JOIN
                         dbo.PunchType ON dbo.Punch.TypeId = dbo.PunchType.Id INNER JOIN
                         dbo.PunchDirectionSalutation AS PDS ON dbo.PunchDirection.Id = PDS.PunchDirectionId AND dbo.PunchType.Id = PDS.PunchTypeId LEFT OUTER JOIN
                         dbo.PunchPair AS ppStop ON dbo.Punch.Time = ppStop.Stop LEFT OUTER JOIN
                         dbo.PunchPair AS ppStart ON dbo.Punch.Time = ppStart.Start
GO
CREATE TRIGGER [dbo].[vPunch#Update]
   ON  [dbo].[vPunch]
   INSTEAD OF INSERT,UPDATE
AS 
BEGIN
	SET NOCOUNT ON;

PRINT object_name(@@PROCID)

IF NOT EXISTS(SELECT * FROM deleted)
	INSERT INTO Punch (Time,DirectionId,TypeId)
	SELECT i.Time,PD.Id,PT.Id FROM inserted i 
	LEFT OUTER JOIN PunchType PT ON i.Type = PT.Name
	LEFT OUTER JOIN PunchDirection PD ON i.Direction = PD.Name
END
GO
EXECUTE sp_addextendedproperty @name = N'MS_DiagramPaneCount', @value = 2, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'vPunch';




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
         Begin Table = "Punch"
            Begin Extent = 
               Top = 26
               Left = 375
               Bottom = 182
               Right = 545
            End
            DisplayFlags = 280
            TopColumn = 0
         End
         Begin Table = "PunchDirection"
            Begin Extent = 
               Top = 4
               Left = 96
               Bottom = 99
               Right = 266
            End
            DisplayFlags = 280
            TopColumn = 0
         End
         Begin Table = "PunchType"
            Begin Extent = 
               Top = 157
               Left = 104
               Bottom = 252
               Right = 274
            End
            DisplayFlags = 280
            TopColumn = 0
         End
         Begin Table = "ppStop"
            Begin Extent = 
               Top = 352
               Left = 660
               Bottom = 447
               Right = 830
            End
            DisplayFlags = 280
            TopColumn = 0
         End
         Begin Table = "ppStart"
            Begin Extent = 
               Top = 18
               Left = 696
               Bottom = 113
               Right = 866
            End
            DisplayFlags = 280
            TopColumn = 0
         End
         Begin Table = "PDS"
            Begin Extent = 
               Top = 160
               Left = 919
               Bottom = 272
               Right = 1100
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
      Begin ColumnWidths = 10
         Width = 284
         Width = 2910
         Width = 1500', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'vPunch';




GO
EXECUTE sp_addextendedproperty @name = N'MS_DiagramPane2', @value = N'Width = 1500
         Width = 1500
         Width = 1500
         Width = 2430
         Width = 3375
         Width = 1500
         Width = 1500
      End
   End
   Begin CriteriaPane = 
      Begin ColumnWidths = 11
         Column = 3015
         Alias = 2160
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
End', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'vPunch';

