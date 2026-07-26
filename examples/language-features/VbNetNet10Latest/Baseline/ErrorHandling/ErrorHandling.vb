Option Strict On

Imports System

Namespace Baseline.ErrorHandling
    Public Module Handling
        ' Structured handling, as in C#, but with one addition C# lacks: a
        ' Catch may carry a When filter, which VB has had since 1.0 while C#
        ' only gained exception filters in 6.0.
        Public Function Structured(code As Integer) As String
            Try
                If code = 1 Then
                    Throw New InvalidOperationException("one")
                End If

                Return "ok"
            Catch ex As InvalidOperationException When code = 1
                Return "filtered:" & ex.Message
            Catch ex As Exception
                Return "general:" & ex.Message
            Finally
                ' Runs on every exit path.
            End Try
        End Function

        ' Unstructured handling, inherited from earlier BASIC. It remains
        ' supported and is worth recognizing in existing code, but structured
        ' handling is the modern choice.
        Public Function Unstructured(value As String) As Integer
            On Error GoTo Failed

            Return CInt(value)

Failed:
            Return -1
        End Function

        ' On Error Resume Next continues at the statement after the failure,
        ' leaving the outcome in Err.
        Public Function ResumeNext(value As String) As Integer
            On Error Resume Next

            Dim parsed As Integer = CInt(value)
            If Err.Number <> 0 Then
                Err.Clear()
                Return -1
            End If

            Return parsed
        End Function
    End Module
End Namespace
