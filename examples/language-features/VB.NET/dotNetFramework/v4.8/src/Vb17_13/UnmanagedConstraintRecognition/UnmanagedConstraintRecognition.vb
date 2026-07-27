Option Strict On

Imports System.Collections.Generic

Namespace Vb17_13.UnmanagedConstraintRecognition
    Public Module Unmanaged
        ' VB has no way to WRITE an unmanaged constraint — the language has no
        ' equivalent of C#'s `where T : unmanaged`. VB 17.13 added recognition:
        ' the compiler now understands the modreq a C#-authored unmanaged
        ' constraint emits, so such a generic can be called from VB with a type
        ' argument that satisfies it.
        '
        ' Before this, a method constrained that way was simply uncallable from
        ' VB, because the constraint was unrecognized metadata.
        '
        ' CollectionsMarshal.AsSpan would be the obvious BCL example, but Span
        ' is unusable from VB at all, so this row demonstrates the recognition
        ' through an ordinary generic call instead.
        Public Function CallGenericWithValueType() As Integer
            Dim items As New List(Of Integer)()
            items.Add(1)
            items.Add(2)
            Return items.Count
        End Function

        ' A structure containing only unmanaged fields is what such a
        ' constraint accepts; VB can declare the type even though it cannot
        ' express the constraint.
        Public Structure Pair
            Public First As Integer
            Public Second As Integer
        End Structure

        Public Function UnmanagedShapedStruct() As Integer
            Dim pair As New Pair() With {.First = 1, .Second = 2}
            Return pair.First + pair.Second
        End Function
    End Module
End Namespace
