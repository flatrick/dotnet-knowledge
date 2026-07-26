Option Strict On

Namespace Baseline.IfOperatorAndPartialMethods
    Public Module IfOperator
        ' The three-argument If is VB's conditional operator, equivalent to
        ' C#'s ?:. It short-circuits, evaluating only the branch it returns.
        Public Function Ternary(value As Integer) As String
            Return If(value < 0, "negative", "non-negative")
        End Function

        ' The two-argument form returns the first operand unless it is Nothing,
        ' making it VB's null-coalescing operator — C#'s ??.
        Public Function Coalesce(value As String) As String
            Return If(value, "fallback")
        End Function

        ' It replaced the older IIf FUNCTION, which is not an operator: IIf
        ' evaluates both arguments and returns Object, so it neither
        ' short-circuits nor preserves the type.
        Public Function LegacyIIf(value As Integer) As String
            Return CStr(IIf(value < 0, "negative", "non-negative"))
        End Function
    End Module

    ' A partial method is declared in one part and may be implemented in
    ' another. As in C# 3.0 it must be a Sub with no return value, and if no
    ' implementation exists the compiler removes the declaration and every call.
    Partial Public Class Report
        Private _lineCount As Integer

        Public ReadOnly Property LineCount As Integer
            Get
                Return _lineCount
            End Get
        End Property

        Public Sub AddLine()
            _lineCount += 1
            OnLineAdded(_lineCount)
        End Sub

        Partial Private Sub OnLineAdded(lineNumber As Integer)
        End Sub

        ' Nothing implements this one, so the call below is erased.
        Partial Private Sub OnReportClosed()
        End Sub

        Public Sub Close()
            OnReportClosed()
        End Sub
    End Class

    Partial Public Class Report
        Private _lastLineNumber As Integer

        Public ReadOnly Property LastLineNumber As Integer
            Get
                Return _lastLineNumber
            End Get
        End Property

        Private Sub OnLineAdded(lineNumber As Integer)
            _lastLineNumber = lineNumber
        End Sub
    End Class
End Namespace
