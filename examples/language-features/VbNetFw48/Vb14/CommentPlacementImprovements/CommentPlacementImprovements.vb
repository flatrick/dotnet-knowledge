Option Strict On

Imports System.Collections.Generic
Imports System.Linq

Namespace Vb14.CommentPlacementImprovements
    Public Module Comments
        ' VB 14 allowed a comment after an implicit line continuation. Before
        ' that, a comment could only follow the LAST line of a wrapped
        ' statement, so annotating one argument meant unwrapping the call.
        Public Function Annotated(left As Integer, ' the left operand
                                  right As Integer) As Integer ' the right one
            Return left + right
        End Function

        ' The same inside a query, where each clause can now carry its own note.
        Public Function Filtered(values As IEnumerable(Of Integer)) As List(Of Integer)
            Dim query = From value In values ' every candidate
                        Where value > 0 ' positives only
                        Order By value ' ascending
                        Select value

            Return query.ToList()
        End Function

        ' ...and inside an initializer.
        Public Function Configured() As List(Of String)
            Return New List(Of String) From {
                "first", ' the head
                "second" ' the tail
            }
        End Function
    End Module
End Namespace
