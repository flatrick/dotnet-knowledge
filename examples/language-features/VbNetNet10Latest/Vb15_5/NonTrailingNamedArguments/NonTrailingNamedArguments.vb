Option Strict On

Namespace Vb15_5.NonTrailingNamedArguments
    Public Module Formatter
        Public Function Format(text As String,
                               Optional upper As Boolean = False,
                               Optional suffix As String = "") As String
            Dim result As String = If(upper, text.ToUpperInvariant(), text)
            Return result & suffix
        End Function

        ' VB names arguments with := rather than C#'s colon. Before 15.5 a named
        ' argument had to be followed only by other named ones; now one may be
        ' named in its own position with the rest left positional.
        Public Function NamedFirst() As String
            Return Format(text:="value", False, "!")
        End Function

        Public Function NamedInMiddle() As String
            Return Format("value", upper:=False, "!")
        End Function

        ' A fully-named call may be reordered, which VB has always allowed.
        Public Function AllNamedReordered() As String
            Return Format(suffix:="?", text:="value", upper:=True)
        End Function
    End Module
End Namespace
