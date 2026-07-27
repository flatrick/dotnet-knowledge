Option Strict On

Imports System.Collections.Generic

Namespace Baseline.ArraysAndCollectionsBasics
    Public Module Arrays
        ' A VB array declaration states the UPPER BOUND, not the length, so
        ' this array holds four elements. That off-by-one difference from C# is
        ' the single most common source of confusion when reading VB.
        Public Function UpperBound() As Integer
            Dim values(3) As Integer
            Return values.Length
        End Function

        ' An initializer supplies the elements and infers the bound.
        Public Function Initialized() As Integer
            Dim values() As Integer = {1, 2, 3}
            Return values.Length
        End Function

        ' Multi-dimensional arrays use a comma; jagged arrays are arrays of
        ' arrays, as in C#.
        Public Function Rectangular() As Integer
            Dim grid(1, 2) As Integer
            grid(0, 0) = 1
            Return grid.Length
        End Function

        Public Function Jagged() As Integer
            Dim rows()() As Integer = New Integer(1)() {}
            rows(0) = New Integer() {1, 2}
            rows(1) = New Integer() {3}
            Return rows(0).Length + rows(1).Length
        End Function

        ' ReDim changes the size at run time; Preserve keeps the contents.
        Public Function Resize() As Integer
            Dim values() As Integer = {1, 2}
            ReDim Preserve values(4)
            Return values.Length
        End Function

        Public Function Generic() As Integer
            Dim items As New List(Of String)()
            items.Add("a")
            items.Add("b")

            Dim lookup As New Dictionary(Of String, Integer)()
            lookup("a") = 1

            Return items.Count + lookup.Count
        End Function
    End Module
End Namespace
