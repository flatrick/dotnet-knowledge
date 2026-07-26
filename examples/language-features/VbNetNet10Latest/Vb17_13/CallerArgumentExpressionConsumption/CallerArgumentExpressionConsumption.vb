Option Strict On

Imports System

Namespace Vb17_13.CallerArgumentExpressionConsumption
    Public Module Guards
        ' VB cannot apply CallerArgumentExpression to its own parameters, but
        ' VB 17.0 taught the compiler to HONOR it when calling a method that
        ' does — filling the parameter with the source text of the argument.
        '
        ' ArgumentNullException.ThrowIfNull is such a BCL method: its second
        ' parameter carries the attribute, so the exception it throws names the
        ' expression the caller wrote.
        Public Function GuardReportsExpression(value As Object) As String
            Try
                ArgumentNullException.ThrowIfNull(value)
                Return "ok"
            Catch ex As ArgumentNullException
                ' ParamName is "value" — the source text, supplied by the
                ' compiler at this call site rather than written by hand.
                Return "null:" & ex.ParamName
            End Try
        End Function

        ' The captured text is the EXPRESSION, not the parameter name, so a
        ' more complex argument is reported as written.
        Public Function ReportsFullExpression(items As String()) As String
            Try
                ArgumentNullException.ThrowIfNull(items?.Length)
                Return "ok"
            Catch ex As ArgumentNullException
                Return "null:" & ex.ParamName
            End Try
        End Function
    End Module
End Namespace
