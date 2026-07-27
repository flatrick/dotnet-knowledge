Option Strict On

Namespace Vb16_0.OptimizedFloatToIntConversion
    Public Module Conversions
        ' VB rounds when converting a floating-point value to an integer, and
        ' uses banker's rounding — halfway values go to the nearest EVEN
        ' integer. C#'s cast truncates instead, so the two languages disagree
        ' on the same input, which is worth knowing when porting.
        '
        ' VB 16.0 changed how this is emitted, not what it produces: the
        ' compiler now generates a direct conversion instead of a helper call.
        ' The observable results are unchanged.
        Public Function RoundsToEven() As Boolean
            Return CInt(2.5) = 2 AndAlso CInt(3.5) = 4
        End Function

        Public Function RoundsRatherThanTruncates() As Boolean
            Return CInt(1.7) = 2
        End Function

        ' Fix truncates toward zero, and Int floors — the explicit forms for
        ' when rounding is not what is wanted.
        Public Function Truncate() As Integer
            Return CInt(Fix(1.7))
        End Function

        Public Function Floor() As Integer
            Return CInt(Int(-1.2))
        End Function

        Public Function TruncationDiffersFromRounding() As Boolean
            Return CInt(Fix(1.7)) <> CInt(1.7)
        End Function
    End Module
End Namespace
