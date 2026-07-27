Option Strict On
Option Infer On

Namespace Vb15.Tuples
    Public Module TupleSamples
        ' A tuple type is written in parentheses, as in C#. VB uses As for the
        ' element types and keeps its own naming style.
        Public Function Unnamed() As (Integer, String)
            Return (1, "one")
        End Function

        ' Named elements. The names are compile-time only; the runtime type is
        ' still ValueTuple.
        Public Function Named() As (count As Integer, name As String)
            Return (2, "two")
        End Function

        Public Function UseNamed() As Integer
            Dim result = Named()
            Return result.count
        End Function

        ' VB has no deconstruction DECLARATION syntax the way C# does; the
        ' elements are read off the tuple by name or position instead.
        Public Function ReadByName() As String
            Dim pair = Named()
            Return pair.name & pair.count.ToString()
        End Function

        Public Function ReadByPosition() As String
            Dim pair = Unnamed()
            Return pair.Item2 & pair.Item1.ToString()
        End Function

        ' The usual reason to reach for one: several results without declaring
        ' a type or using ByRef parameters.
        Public Function MinMax(values As Integer()) As (min As Integer, max As Integer)
            Dim lowest As Integer = values(0)
            Dim highest As Integer = values(0)

            For Each value As Integer In values
                If value < lowest Then
                    lowest = value
                End If

                If value > highest Then
                    highest = value
                End If
            Next

            Return (lowest, highest)
        End Function
    End Module
End Namespace
