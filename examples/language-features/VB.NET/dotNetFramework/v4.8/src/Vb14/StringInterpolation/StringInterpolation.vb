Option Strict On

Namespace Vb14.StringInterpolation
    Public Module Interpolation
        ' An interpolated string is compiled into a formatting call. VB uses the
        ' same $"" syntax C# 6.0 introduced, in the same release wave.
        Public Function Simple(name As String, count As Integer) As String
            Return $"{name} has {count} items"
        End Function

        ' A hole may carry a format specifier after a colon...
        Public Function WithFormat(value As Double) As String
            Return $"{value:F2}"
        End Function

        ' ...and an alignment before it. Negative pads on the right.
        Public Function WithAlignment(label As String, value As Integer) As String
            Return $"{label,-10}|{value,5}"
        End Function

        Public Function WithExpression(left As Integer, right As Integer) As String
            Return $"{left} + {right} = {left + right}"
        End Function

        ' Doubling a brace escapes it.
        Public Function EscapedBraces(value As Integer) As String
            Return $"{{{value}}}"
        End Function

        ' VB has no verbatim-string prefix, because its literals never process
        ' backslash escapes in the first place — a backslash is always literal.
        Public Function BackslashIsLiteral(name As String) As String
            Return $"C:\logs\{name}.txt"
        End Function
    End Module
End Namespace
