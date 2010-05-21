Option Explicit On
Public Class Reg

  Private Const MaxO As Integer = 25
  Private GlobalO As Integer '"Ordnung" = degree of the polynom expected
  Private Finished As Boolean

  Private SumX#(0 To 2 * MaxO)
  Private SumYX#(0 To MaxO)
  Private M#(0 To MaxO, 0 To MaxO + 1)
  Private C#(0 To MaxO) 'coefficients in: Y = C(0)*X^0 + C(1)*X^1 + C(2)*X^2 + ...

  Private Sub GaussSolve(ByVal O As Integer)
    'gauss algorithm implementation,
    'following R.Sedgewick's "Algorithms in C", Addison-Wesley, with minor modifications
    Dim i As Integer, j As Integer, k As Integer, iMax As Integer, T As Double, O1 As Integer
    O1 = O + 1
    'first triangulize the matrix
    For i = 0 To O
      iMax = i : T = Math.Abs(M(iMax, i))
      For j = i + 1 To O 'find the line with the largest absvalue in this row
        If T < Math.Abs(M(j, i)) Then iMax = j : T = Math.Abs(M(iMax, i))
      Next j
      If i < iMax Then 'exchange the two lines
        For k = i To O1
          T = M(i, k)
          M(i, k) = M(iMax, k)
          M(iMax, k) = T
        Next k
      End If
      For j = i + 1 To O 'scale all following lines to have a leading zero
        T = M(j, i) / M(i, i)
        M(j, i) = 0.0#
        For k = i + 1 To O1
          M(j, k) = M(j, k) - M(i, k) * T
        Next k
      Next j
    Next i
    'then substitute the coefficients
    For j = O To 0 Step -1
      T = M(j, O1)
      For k = j + 1 To O
        T = T - M(j, k) * C(k)
      Next k
      C(j) = T / M(j, j)
    Next j
    Finished = True
  End Sub

  Private Sub BuildMatrix(ByVal O As Integer)
    Dim i As Integer, k As Integer, O1 As Integer
    O1 = O + 1
    For i = 0 To O
      For k = 0 To O
        M(i, k) = SumX(i + k)
      Next k
      M(i, O1) = SumYX(i)
    Next i
  End Sub

  Private Sub FinalizeMatrix(ByVal O As Integer)
    Dim i As Integer, O1 As Integer
    O1 = O + 1
    For i = 0 To O
      M(i, O1) = SumYX(i)
    Next i
  End Sub

  Private Sub Solve()
    Dim O As Integer
    O = GlobalO
    If XYCount() <= O Then O = XYCount() - 1
    If O < 0 Then Exit Sub
    BuildMatrix(O)
    Try
      GaussSolve(O)
      While (Err.Number <> 0) And (1 < O)
        Err.Clear()
        C(0) = 0.0#
        O = O - 1
        FinalizeMatrix(O)
      End While
    Catch
    End Try
  End Sub

  Public Sub New()
    Init()
    GlobalO = 2
  End Sub
  Public Sub New(ByVal degree As Integer)
    Init()
    GlobalO = degree
  End Sub

  Public Sub Init()
    Dim i As Integer
    Finished = False
    For i = 0 To MaxO
      SumX(i) = 0.0#
      SumX(i + MaxO) = 0.0#
      SumYX(i) = 0.0#
      C(i) = 0.0#
    Next i
  End Sub

  Public Function Coeff(ByVal Exponent As Integer) As Double
    Dim Ex As Integer, O As Integer
    If Not Finished Then Solve()
    Ex = Math.Abs(Exponent)
    O = GlobalO
    If XYCount() <= O Then O = XYCount() - 1
    If O < Ex Then Return 0 Else Return C(Ex)
  End Function

  Public Function Degree() As Integer
    Return GlobalO
  End Function
  Public Sub Degree(ByVal NewVal As Integer)
    If NewVal < 0 Or MaxO < NewVal Then
      Err.Raise(6000, "RegressionObject", NewVal & " is an invalid property value! Use 0<= Degree <= " & MaxO)
      Return
    End If
    Init()
    GlobalO = NewVal
  End Sub

  Public Function XYCount() As Integer
    Return CInt(SumX(0))
  End Function

  Public Sub XYAdd(ByVal NewX As Double, ByVal NewY As Double)
    Dim i As Integer, j As Integer, TX As Double, Max2O As Integer
    Finished = False
    Max2O = 2 * GlobalO
    TX = 1.0#
    SumX(0) = SumX(0) + 1
    SumYX(0) = SumYX(0) + NewY
    For i = 1 To GlobalO
      TX = TX * NewX
      SumX(i) = SumX(i) + TX
      SumYX(i) = SumYX(i) + NewY * TX
    Next i
    For i = GlobalO + 1 To Max2O
      TX = TX * NewX
      SumX(i) = SumX(i) + TX
    Next i
  End Sub

  Public Function RegVal(ByVal X As Double) As Double
    Dim i As Integer, O As Integer, rv As Double
    If Not Finished Then Solve()
    rv = 0.0
    O = GlobalO
    If XYCount() <= O Then O = XYCount() - 1
    For i = 0 To O
      rv = RegVal + C(i) * X ^ i
    Next i
    Return rv
  End Function

End Class
