Option Strict On

Namespace Baseline.NullableValueTypes
    Public Module Nullables
        ' VB spells a nullable value type with a trailing question mark on the
        ' NAME, not the type — Dim x? As Integer — or with Nullable(Of T).
        Public Function Declared() As Integer?
            Dim value As Integer? = Nothing
            Return value
        End Function

        Public Function ShorthandOnName() As Integer
            Dim value? As Integer = 5
            Return value.Value
        End Function

        Public Function Generic() As Integer?
            Dim value As Nullable(Of Integer) = 7
            Return value
        End Function

        ' HasValue and Value work as in C#.
        Public Function ReadSafely(value As Integer?) As Integer
            If value.HasValue Then
                Return value.Value
            End If

            Return 0
        End Function

        ' Nothing is VB's null AND its default value, so comparing a nullable
        ' to Nothing tests HasValue rather than performing a reference check.
        Public Function IsUnset(value As Integer?) As Boolean
            Return value Is Nothing
        End Function

        ' Arithmetic lifts over nullables: any Nothing operand yields Nothing.
        Public Function Lifted(left As Integer?, right As Integer?) As Integer?
            Return left + right
        End Function

        ' The two-argument If supplies a fallback.
        Public Function OrDefault(value As Integer?) As Integer
            Return If(value, 0)
        End Function
    End Module
End Namespace
