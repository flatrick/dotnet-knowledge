Option Strict On

Imports System.Collections.Generic
Imports System.Runtime.CompilerServices

Namespace Baseline.ExtensionMethods
    ' An extension method is marked with the Extension attribute and must live
    ' in a Module. C# uses a `this` modifier on the first parameter instead;
    ' VB's marker is the attribute.
    Public Module StringExtensions
        <Extension()>
        Public Function IsNullOrBlank(value As String) As Boolean
            Return value Is Nothing OrElse value.Trim().Length = 0
        End Function

        <Extension()>
        Public Function Repeat(value As String, times As Integer) As String
            Dim result As String = String.Empty

            For i As Integer = 1 To times
                result &= value
            Next

            Return result
        End Function
    End Module

    Public Module EnumerableExtensions
        ' Extending an interface makes the method available on every
        ' implementing type.
        <Extension()>
        Public Function CountEven(values As IEnumerable(Of Integer)) As Integer
            Dim count As Integer = 0

            For Each value As Integer In values
                If value Mod 2 = 0 Then
                    count += 1
                End If
            Next

            Return count
        End Function
    End Module

    Public Module Usage
        Public Function CallAsInstanceMethod() As String
            Return "ab".Repeat(3)
        End Function

        ' The same call written as the plain invocation it compiles to.
        Public Function CallAsPlainMethod() As String
            Return StringExtensions.Repeat("ab", 3)
        End Function

        Public Function Evens(values As List(Of Integer)) As Integer
            Return values.CountEven()
        End Function
    End Module
End Namespace
