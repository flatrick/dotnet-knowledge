Option Strict On

Imports System.Collections.Generic
Imports System.Linq

Namespace Vb16_0.CommentsInMorePlaces
    Public Module Comments
        ' VB 14 allowed a comment after an implicit line continuation, and later
        ' versions widened the set of positions. The two forms below are the
        ' ones that compile; each was verified rather than assumed, and the
        ' rejected forms are named at the bottom with the errors they produce.

        ' A trailing comment on each clause of a multi-line query.
        Public Function InQuery(values As IEnumerable(Of Integer)) As List(Of Integer)
            Dim query = From value In values ' source
                        Where value > 0 ' positives only
                        Order By value ' ascending
                        Select value ' projection

            Return query.ToList()
        End Function

        ' A trailing comment on an argument of a multi-line call.
        Public Function InArgumentList() As String
            Return String.Join(", ", ' separator
                               New String() {"a", "b"})
        End Function

        ' Three placements remain rejected, and knowing which is the practical
        ' value of this row:
        '
        '   - A comment on its own line BETWEEN query clauses. The parser reads
        '     the next clause keyword as a new statement, so Select becomes
        '     Select Case (BC30095, BC30058, BC30800).
        '   - A comment on its own line between the operands of a continued
        '     expression (BC30201, BC30801).
        '   - A comment on its own line inside a From initializer
        '     (BC30035, BC30201, BC30370).
        '
        ' In every one of those, moving the comment to the end of the preceding
        ' line compiles.
    End Module
End Namespace
