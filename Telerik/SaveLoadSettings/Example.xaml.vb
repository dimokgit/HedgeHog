Imports System.Collections
Imports System.Collections.Generic
Imports System.Collections.ObjectModel
Imports System.Windows.Controls
Imports System.Linq
Imports System.Windows.Data
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports System.IO
Imports System.Text
Imports System.Xml
Imports System.Windows
Imports System.Windows.Resources
Imports System.Globalization
Imports System.ComponentModel
Imports Telerik.Windows.Controls.GridView.Settings

Namespace Telerik.Windows.Examples.GridView.SaveLoadSettings
	''' <summary>
	''' Interaction logic for Example.xaml
	''' </summary>
	Public Partial Class Example
		Private settings As RadGridViewSettings = Nothing

		Public Sub New()
			Me.InitializeComponent()

			AddHandler Me.Unloaded, AddressOf Example_Unloaded
			AddHandler Me.Loaded, AddressOf Example_Loaded
		End Sub

		Private Sub Example_Loaded(sender As Object, e As EventArgs)
			settings = New RadGridViewSettings(RadGridView1)
			settings.LoadState()
		End Sub

		Private Sub Example_Unloaded(sender As Object, e As EventArgs)
			If settings IsNot Nothing Then
				settings.SaveState()
			End If
		End Sub
	End Class
End Namespace
