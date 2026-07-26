Option Strict On

Namespace Vb14.MultiLineStringLiterals
    Public Module MultiLine
        ' A string literal may span source lines directly. Before VB 14 a
        ' multi-line value needed concatenation with vbCrLf or a continuation,
        ' because a literal could not contain a newline.
        Public Function Block() As String
            Return "first line
second line
third line"
        End Function

        ' The newline in the source is a newline in the value, so no escape and
        ' no vbCrLf constant is involved.
        Public Function ContainsNewline() As Boolean
            Return Block().Contains(vbLf) OrElse Block().Contains(vbCrLf)
        End Function

        ' The older idiom, kept as contrast.
        Public Function Concatenated() As String
            Return "first line" & vbCrLf &
                   "second line" & vbCrLf &
                   "third line"
        End Function

        ' Interpolation works across lines too.
        Public Function Interpolated(name As String) As String
            Return $"name: {name}
kind: sample"
        End Function
    End Module
End Namespace
