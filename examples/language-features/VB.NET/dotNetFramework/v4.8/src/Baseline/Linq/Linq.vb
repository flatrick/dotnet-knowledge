Option Strict On

Imports System.Collections.Generic
Imports System.Linq

Namespace Baseline.Linq
    Public Module Queries
        ' VB's query syntax is closer to SQL than C#'s: it has more clauses,
        ' and Select is optional when the range variable itself is the result.
        Public Function EvenSquares(values As IEnumerable(Of Integer)) As List(Of Integer)
            Dim query = From value In values
                        Where value Mod 2 = 0
                        Order By value
                        Select value * value

            Return query.ToList()
        End Function

        ' Aggregate has no C# query-syntax equivalent at all.
        Public Function Total(values As IEnumerable(Of Integer)) As Integer
            Return Aggregate value In values Into Sum(value)
        End Function

        ' Group By ... Into, with an aggregate per group.
        Public Function GroupSizes(names As IEnumerable(Of String)) As List(Of Integer)
            Dim query = From name In names
                        Group name By Length = name.Length Into Group, Count()
                        Select Count

            Return query.ToList()
        End Function

        ' Distinct, Skip, and Take are clauses in VB rather than method calls.
        Public Function Page(values As IEnumerable(Of Integer)) As List(Of Integer)
            Dim query = From value In values
                        Distinct
                        Skip 1
                        Take 2

            Return query.ToList()
        End Function

        ' Join correlates two sequences.
        Public Function JoinOnLength(ids As IEnumerable(Of Integer), names As IEnumerable(Of String)) As List(Of String)
            Dim query = From id In ids
                        Join name In names On id Equals name.Length
                        Select id.ToString() & "=" & name

            Return query.ToList()
        End Function

        ' Let introduces a computed range variable.
        Public Function WithLet(names As IEnumerable(Of String)) As List(Of String)
            Dim query = From name In names
                        Let size = name.Length
                        Where size > 2
                        Select name & ":" & size.ToString()

            Return query.ToList()
        End Function
    End Module
End Namespace
