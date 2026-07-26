Option Strict On

Imports System.Collections.Generic
Imports System.Linq

Namespace Baseline.ImplicitLineContinuation
    Public Module Continuation
        ' Before VS2010 every wrapped line needed a trailing underscore. Since
        ' then the compiler infers the continuation after tokens that cannot
        ' end a statement — a comma, an operator, an opening bracket, and the
        ' clause keywords of a query.
        Public Function AfterComma(left As Integer,
                                   middle As Integer,
                                   right As Integer) As Integer
            Return left + middle + right
        End Function

        Public Function AfterOperator(value As Integer) As Integer
            Return value +
                   value *
                   2
        End Function

        Public Function InQuery(values As IEnumerable(Of Integer)) As List(Of Integer)
            Dim query = From value In values
                        Where value > 0
                        Order By value
                        Select value

            Return query.ToList()
        End Function

        Public Function InInitializer() As List(Of String)
            Return New List(Of String) From {
                "first",
                "second"
            }
        End Function

        ' The explicit underscore remains legal, and is still required where no
        ' rule applies — for example before a keyword that could begin a new
        ' statement.
        Public Function ExplicitUnderscore(value As Integer) As Integer
            Dim result As Integer = _
                value * 2

            Return result
        End Function
    End Module
End Namespace
