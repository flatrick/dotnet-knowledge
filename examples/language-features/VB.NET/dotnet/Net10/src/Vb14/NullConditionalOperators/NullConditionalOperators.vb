Option Strict On

Namespace Vb14.NullConditionalOperators
    Public Class Node
        Public Property NextNode As Node
        Public Property Name As String
        Public Property Values As Integer()
    End Class

    Public Module NullConditional
        ' ?. yields Nothing instead of throwing when the left operand is
        ' Nothing — VB's spelling of C#'s ?. operator.
        Public Function NameOrNothing(node As Node) As String
            Return node?.Name
        End Function

        ' Chained access short-circuits at the first Nothing.
        Public Function ChainedName(node As Node) As String
            Return node?.NextNode?.Name
        End Function

        ' The indexing form is ?( ) rather than C#'s ?[ ], because VB indexes
        ' with parentheses.
        Public Function FirstValue(node As Node) As Integer?
            Return node?.Values?(0)
        End Function

        ' The two-argument If supplies the fallback, collapsing the lifted type.
        Public Function CountOrZero(node As Node) As Integer
            Return If(node?.Values?.Length, 0)
        End Function

        ' A dictionary-style default member access uses the same form.
        Public Function NameLength(node As Node) As Integer?
            Return node?.Name?.Length
        End Function
    End Module
End Namespace
