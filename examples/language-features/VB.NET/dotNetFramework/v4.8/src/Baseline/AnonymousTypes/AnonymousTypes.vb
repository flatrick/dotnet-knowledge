Option Strict On
Option Infer On

Imports System.Collections.Generic

Namespace Baseline.AnonymousTypes
    Public Module Anonymous
        ' An anonymous type has no writable name, so Option Infer must be On.
        Public Function Describe() As String
            Dim point = New With {.X = 1, .Y = 2}
            Return point.X.ToString() & "," & point.Y.ToString()
        End Function

        ' VB anonymous-type members are MUTABLE by default, unlike C#'s, which
        ' are always read-only. Key marks a member immutable and includes it in
        ' the generated Equals and GetHashCode.
        Public Function MutableByDefault() As Integer
            Dim item = New With {.Count = 1}
            item.Count = 2
            Return item.Count
        End Function

        ' Only Key members take part in equality, so these two are equal even
        ' though their Count differs.
        Public Function KeyEquality() As Boolean
            Dim first = New With {Key .Name = "a", .Count = 1}
            Dim second = New With {Key .Name = "a", .Count = 2}
            Return first.Equals(second)
        End Function

        Public Function Projection(names As String()) As List(Of String)
            Dim projected As New List(Of String)()

            For Each name As String In names
                Dim item = New With {Key .Original = name, .Length = name.Length}
                projected.Add(item.Original & ":" & item.Length.ToString())
            Next

            Return projected
        End Function
    End Module
End Namespace
