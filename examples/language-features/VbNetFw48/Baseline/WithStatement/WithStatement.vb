Option Strict On

Namespace Baseline.WithStatement
    Public Class Settings
        Public Property Theme As String
        Public Property Retries As Integer
        Public Property Region As String
    End Class

    Public Module WithSamples
        ' With names an expression once, and every member access inside the
        ' block beginning with a dot applies to it. C# has no equivalent
        ' statement; an object initializer covers only the construction case.
        Public Function Configure() As Settings
            Dim settings As New Settings()

            With settings
                .Theme = "dark"
                .Retries = 3
                .Region = "global"
            End With

            Return settings
        End Function

        ' The expression is evaluated once, which matters when it is expensive
        ' or has side effects.
        Public Function ReadThrough(settings As Settings) As String
            With settings
                Return .Theme & ":" & .Region & ":" & .Retries.ToString()
            End With
        End Function

        ' With blocks nest, and the innermost dot wins.
        Public Function Nested() As String
            Dim outer As New Settings() With {.Theme = "outer"}
            Dim inner As New Settings() With {.Theme = "inner"}

            With outer
                With inner
                    Return .Theme
                End With
            End With
        End Function
    End Module
End Namespace
