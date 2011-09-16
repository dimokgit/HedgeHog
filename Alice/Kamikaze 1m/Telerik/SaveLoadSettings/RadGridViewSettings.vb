Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Configuration
Imports System.ComponentModel
Imports System.Windows
Imports System.Windows.Data
Imports Telerik.Windows.Controls
Imports Telerik.Windows.Data
Imports Telerik.Windows.Controls.GridView
Imports System.Windows.Controls
Imports System.Runtime.Serialization
Imports System.IO
Imports System.IO.IsolatedStorage

Namespace Telerik.Windows.Controls.GridView.Settings
	Public Class RadGridViewSettings
				'
		Public Sub New()
		End Sub

		Public Class RadGridViewApplicationSettings
			Inherits Dictionary(Of String, Object)
			Private settings As RadGridViewSettings

			Private serializer As DataContractSerializer = Nothing

					'
			Public Sub New()
			End Sub

			Public Sub New(settings As RadGridViewSettings)
				Me.settings = settings

				Dim types As New List(Of Type)()
				types.Add(GetType(List(Of ColumnSetting)))
				types.Add(GetType(List(Of FilterSetting)))
				types.Add(GetType(List(Of GroupSetting)))
				types.Add(GetType(List(Of SortSetting)))
				types.Add(GetType(List(Of PropertySetting)))

				Me.serializer = New DataContractSerializer(GetType(RadGridViewApplicationSettings), types)
			End Sub

			Public ReadOnly Property PersistID() As String
				Get
					If Not ContainsKey("PersistID") AndAlso settings.grid IsNot Nothing Then
						Me("PersistID") = settings.grid.Name
					End If

					Return DirectCast(Me("PersistID"), String)
				End Get
			End Property

			Public Property FrozenColumnCount() As Integer
				Get
					If Not ContainsKey("FrozenColumnCount") Then
						Me("FrozenColumnCount") = 0
					End If

					Return CInt(Me("FrozenColumnCount"))
				End Get
				Set
					Me("FrozenColumnCount") = value
				End Set
			End Property

			Public ReadOnly Property ColumnSettings() As List(Of ColumnSetting)
				Get
					If Not ContainsKey("ColumnSettings") Then
						Me("ColumnSettings") = New List(Of ColumnSetting)()
					End If

					Return DirectCast(Me("ColumnSettings"), List(Of ColumnSetting))
				End Get
			End Property

			Public ReadOnly Property SortSettings() As List(Of SortSetting)
				Get
					If Not ContainsKey("SortSettings") Then
						Me("SortSettings") = New List(Of SortSetting)()
					End If

					Return DirectCast(Me("SortSettings"), List(Of SortSetting))
				End Get
			End Property

			Public ReadOnly Property GroupSettings() As List(Of GroupSetting)
				Get
					If Not ContainsKey("GroupSettings") Then
						Me("GroupSettings") = New List(Of GroupSetting)()
					End If

					Return DirectCast(Me("GroupSettings"), List(Of GroupSetting))
				End Get
			End Property

			Public ReadOnly Property FilterSettings() As List(Of FilterSetting)
				Get
					If Not ContainsKey("FilterSettings") Then
						Me("FilterSettings") = New List(Of FilterSetting)()
					End If

					Return DirectCast(Me("FilterSettings"), List(Of FilterSetting))
				End Get
			End Property

			Public Sub Reload()
				Try
					Using file As IsolatedStorageFile = IsolatedStorageFile.GetUserStoreForApplication()
						Using stream As New IsolatedStorageFileStream(PersistID, FileMode.Open, file)
							If stream.Length > 0 Then
								Dim loaded As RadGridViewApplicationSettings = DirectCast(serializer.ReadObject(stream), RadGridViewApplicationSettings)

								FrozenColumnCount = loaded.FrozenColumnCount

								ColumnSettings.Clear()
								For Each cs As ColumnSetting In loaded.ColumnSettings
									ColumnSettings.Add(cs)
								Next

								FilterSettings.Clear()
								For Each fs As FilterSetting In loaded.FilterSettings
									FilterSettings.Add(fs)
								Next

								GroupSettings.Clear()
								For Each gs As GroupSetting In loaded.GroupSettings
									GroupSettings.Add(gs)
								Next

								SortSettings.Clear()
								For Each ss As SortSetting In loaded.SortSettings
									SortSettings.Add(ss)
								Next
							End If
						End Using
					End Using

				Catch
				End Try
			End Sub

			Public Sub Reset()
				Try
					Using file As IsolatedStorageFile = IsolatedStorageFile.GetUserStoreForApplication()
						file.DeleteFile(PersistID)
					End Using
						'
				Catch
				End Try
			End Sub

			Public Sub Save()
				Try
					Using file As IsolatedStorageFile = IsolatedStorageFile.GetUserStoreForApplication()
						Using stream As New IsolatedStorageFileStream(PersistID, FileMode.Create, file)
							serializer.WriteObject(stream, Me)
						End Using
					End Using
						'
				Catch
				End Try
			End Sub
		End Class

		Private grid As RadGridView = Nothing

		Public Sub New(grid As RadGridView)
			Me.grid = grid
		End Sub

		Public Shared ReadOnly IsEnabledProperty As DependencyProperty = DependencyProperty.RegisterAttached("IsEnabled", GetType(Boolean), GetType(RadGridViewSettings), New PropertyMetadata(New PropertyChangedCallback(AddressOf OnIsEnabledPropertyChanged)))

		Public Shared Function GetIsEnabled(dependencyObject As DependencyObject) As Boolean
			Return CBool(dependencyObject.GetValue(IsEnabledProperty))
		End Function

		Public Shared Sub SetIsEnabled(dependencyObject As DependencyObject, enabled As Boolean)
			dependencyObject.SetValue(IsEnabledProperty, enabled)
		End Sub

		Private Shared Sub OnIsEnabledPropertyChanged(dependencyObject As DependencyObject, e As DependencyPropertyChangedEventArgs)
			Dim grid As RadGridView = TryCast(dependencyObject, RadGridView)
			If grid IsNot Nothing Then
				If CBool(e.NewValue) Then
					Dim settings As New RadGridViewSettings(grid)
					settings.Attach()
				End If
			End If
		End Sub

		Public Overridable Sub LoadState()
			Try
				Settings.Reload()
			Catch
				Settings.Reset()
			End Try

			If Me.grid IsNot Nothing Then
				grid.FrozenColumnCount = Settings.FrozenColumnCount

				If Settings.ColumnSettings.Count > 0 Then
					For Each setting As ColumnSetting In Settings.ColumnSettings
						Dim currentSetting As ColumnSetting = setting

						Dim column As GridViewDataColumn = (From c In grid.Columns.OfType(Of GridViewDataColumn)() _
							Where c.UniqueName = currentSetting.UniqueName _
							Select c).FirstOrDefault()

						If column IsNot Nothing Then
							If currentSetting.DisplayIndex IsNot Nothing Then
								column.DisplayIndex = currentSetting.DisplayIndex.Value
							End If

							If setting.Width IsNot Nothing Then
								column.Width = New GridViewLength(setting.Width.Value)
							End If
						End If
					Next
				End If
				Using grid.DeferRefresh()
					If Settings.SortSettings.Count > 0 Then
						grid.SortDescriptors.Clear()

						For Each setting As SortSetting In Settings.SortSettings
							Dim d As New Telerik.Windows.Data.SortDescriptor()
							d.Member = setting.PropertyName
							d.SortDirection = setting.SortDirection

							grid.SortDescriptors.Add(d)
						Next
					End If

					If Settings.GroupSettings.Count > 0 Then
						grid.GroupDescriptors.Clear()

						For Each setting As GroupSetting In Settings.GroupSettings
							Dim d As New Telerik.Windows.Data.GroupDescriptor()
							d.Member = setting.PropertyName
							d.SortDirection = setting.SortDirection
							d.DisplayContent = setting.DisplayContent

							grid.GroupDescriptors.Add(d)
						Next
					End If

					If Settings.FilterSettings.Count > 0 Then
						For Each setting As FilterSetting In Settings.FilterSettings
							Dim currentSetting As FilterSetting = setting

							Dim matchingColumn As GridViewDataColumn = (From column In grid.Columns.OfType(Of GridViewDataColumn)() _
								Where column.DataMemberBinding.Path.Path = currentSetting.PropertyName _
								Select column).FirstOrDefault()

							If matchingColumn IsNot Nothing Then
								Dim cfd As New ColumnFilterDescriptor(matchingColumn)

								cfd.FieldFilter.Filter1.Member = setting.Filter1.Member
								cfd.FieldFilter.Filter1.[Operator] = setting.Filter1.[Operator]
								cfd.FieldFilter.Filter1.Value = setting.Filter1.Value

								cfd.FieldFilter.Filter2.Member = setting.Filter2.Member
								cfd.FieldFilter.Filter2.[Operator] = setting.Filter2.[Operator]
								cfd.FieldFilter.Filter2.Value = setting.Filter2.Value

								For Each descriptor As Telerik.Windows.Data.FilterDescriptor In setting.SelectedDistinctValues
									cfd.DistinctFilter.FilterDescriptors.Add(descriptor)
								Next

								Me.grid.FilterDescriptors.Add(cfd)
							End If
						Next
					End If
				End Using
			End If
		End Sub

		Public Overridable Sub ResetState()
			Settings.Reset()
		End Sub

		Public Overridable Sub SaveState()
			Settings.Reset()

			If grid IsNot Nothing Then
				If grid.Columns IsNot Nothing Then
					Settings.ColumnSettings.Clear()

					For Each column As GridViewColumn In grid.Columns
						If TypeOf column Is GridViewDataColumn Then
							Dim dataColumn As GridViewDataColumn = DirectCast(column, GridViewDataColumn)

							Dim setting As New ColumnSetting()
							setting.PropertyName = dataColumn.DataMemberBinding.Path.Path
							setting.UniqueName = dataColumn.UniqueName
							setting.Header = dataColumn.Header
							setting.Width = dataColumn.ActualWidth
							setting.DisplayIndex = dataColumn.DisplayIndex

							Settings.ColumnSettings.Add(setting)
						End If
					Next
				End If

				If grid.FilterDescriptors IsNot Nothing Then
					Settings.FilterSettings.Clear()

					For Each cfd As IColumnFilterDescriptor In grid.FilterDescriptors.OfType(Of IColumnFilterDescriptor)()
						Dim setting As New FilterSetting()

						setting.Filter1 = New Telerik.Windows.Data.FilterDescriptor()
						setting.Filter1.Member = cfd.FieldFilter.Filter1.Member
						setting.Filter1.[Operator] = cfd.FieldFilter.Filter1.[Operator]
						setting.Filter1.Value = cfd.FieldFilter.Filter1.Value
						setting.Filter1.MemberType = Nothing

						setting.Filter2 = New Telerik.Windows.Data.FilterDescriptor()
						setting.Filter2.Member = cfd.FieldFilter.Filter2.Member
						setting.Filter2.[Operator] = cfd.FieldFilter.Filter2.[Operator]
						setting.Filter2.Value = cfd.FieldFilter.Filter2.Value
						setting.Filter2.MemberType = Nothing

						For Each fd As Telerik.Windows.Data.FilterDescriptor In cfd.DistinctFilter.FilterDescriptors.OfType(Of Telerik.Windows.Data.FilterDescriptor)()
							Dim newFd As New Telerik.Windows.Data.FilterDescriptor()
							newFd.Member = fd.Member
							newFd.[Operator] = fd.[Operator]
							newFd.Value = fd.Value
							newFd.MemberType = Nothing
							setting.SelectedDistinctValues.Add(newFd)
						Next

						setting.PropertyName = cfd.Column.DataMemberBinding.Path.Path

						Settings.FilterSettings.Add(setting)
					Next
				End If

				If grid.SortDescriptors IsNot Nothing Then
					Settings.SortSettings.Clear()

					For Each d As Telerik.Windows.Data.SortDescriptor In grid.SortDescriptors
						Dim setting As New SortSetting()

						setting.PropertyName = d.Member
						setting.SortDirection = d.SortDirection

						Settings.SortSettings.Add(setting)
					Next
				End If

				If grid.GroupDescriptors IsNot Nothing Then
					Settings.GroupSettings.Clear()

					For Each d As Telerik.Windows.Data.GroupDescriptor In grid.GroupDescriptors
						Dim setting As New GroupSetting()

						setting.PropertyName = d.Member
						setting.SortDirection = d.SortDirection
						setting.DisplayContent = d.DisplayContent

						Settings.GroupSettings.Add(setting)
					Next
				End If

				Settings.FrozenColumnCount = grid.FrozenColumnCount
			End If

			Settings.Save()
		End Sub

		Private Sub Attach()
			If Me.grid IsNot Nothing Then
				AddHandler Me.grid.LayoutUpdated, New EventHandler(AddressOf LayoutUpdated)
				AddHandler Me.grid.Loaded, AddressOf Loaded
				AddHandler Application.Current.[Exit], AddressOf Current_Exit
			End If
		End Sub

		Private Sub Current_Exit(sender As Object, e As EventArgs)
			SaveState()
		End Sub

		Private Sub Loaded(sender As Object, e As EventArgs)
			LoadState()
		End Sub

		Private Sub LayoutUpdated(sender As Object, e As EventArgs)
			If grid.Parent Is Nothing Then
				SaveState()
			End If
		End Sub

		Private gridViewApplicationSettings As RadGridViewApplicationSettings = Nothing

		Protected Overridable Function CreateRadGridViewApplicationSettingsInstance() As RadGridViewApplicationSettings
			Return New RadGridViewApplicationSettings(Me)
		End Function

		Protected ReadOnly Property Settings() As RadGridViewApplicationSettings
			Get
				If gridViewApplicationSettings Is Nothing Then
					gridViewApplicationSettings = CreateRadGridViewApplicationSettingsInstance()
				End If
				Return gridViewApplicationSettings
			End Get
		End Property
	End Class

	Public Class PropertySetting
		Private _PropertyName As String
		Public Property PropertyName() As String
			Get
				Return _PropertyName
			End Get
			Set
				_PropertyName = value
			End Set
		End Property
	End Class

	Public Class SortSetting
		Inherits PropertySetting
		Private _SortDirection As ListSortDirection
		Public Property SortDirection() As ListSortDirection
			Get
				Return _SortDirection
			End Get
			Set
				_SortDirection = value
			End Set
		End Property
	End Class

	Public Class GroupSetting
		Inherits PropertySetting
		Private _DisplayContent As Object
		Public Property DisplayContent() As Object
			Get
				Return _DisplayContent
			End Get
			Set
				_DisplayContent = value
			End Set
		End Property

		Private _SortDirection As System.Nullable(Of ListSortDirection)
		Public Property SortDirection() As System.Nullable(Of ListSortDirection)
			Get
				Return _SortDirection
			End Get
			Set
				_SortDirection = value
			End Set
		End Property
	End Class

	Public Class FilterSetting
		Inherits PropertySetting
		Private _SelectedDistinctValues As List(Of Telerik.Windows.Data.FilterDescriptor)
		Public ReadOnly Property SelectedDistinctValues() As List(Of Telerik.Windows.Data.FilterDescriptor)
			Get
				If _SelectedDistinctValues Is Nothing Then
					_SelectedDistinctValues = New List(Of Telerik.Windows.Data.FilterDescriptor)()
				End If
				Return _SelectedDistinctValues
			End Get
		End Property

		Private _Filter1 As Telerik.Windows.Data.FilterDescriptor
		Public Property Filter1() As Telerik.Windows.Data.FilterDescriptor
			Get
				Return _Filter1
			End Get
			Set
				_Filter1 = value
			End Set
		End Property

		Private _Filter2 As Telerik.Windows.Data.FilterDescriptor
		Public Property Filter2() As Telerik.Windows.Data.FilterDescriptor
			Get
				Return _Filter2
			End Get
			Set
				_Filter2 = value
			End Set
		End Property
	End Class

	Public Class ColumnSetting
		Inherits PropertySetting
		Private _UniqueName As String
		Public Property UniqueName() As String
			Get
				Return _UniqueName
			End Get
			Set
				_UniqueName = value
			End Set
		End Property

		Private _Header As Object
		Public Property Header() As Object
			Get
				Return _Header
			End Get
			Set
				_Header = value
			End Set
		End Property

		Private _Width As System.Nullable(Of Double)
		Public Property Width() As System.Nullable(Of Double)
			Get
				Return _Width
			End Get
			Set
				_Width = value
			End Set
		End Property

		Private _DisplayIndex As System.Nullable(Of Integer)
		Public Property DisplayIndex() As System.Nullable(Of Integer)
			Get
				Return _DisplayIndex
			End Get
			Set
				_DisplayIndex = value
			End Set
		End Property
	End Class
End Namespace
