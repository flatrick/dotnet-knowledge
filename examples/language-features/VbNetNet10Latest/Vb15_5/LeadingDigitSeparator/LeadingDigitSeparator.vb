Option Strict On

Namespace Vb15_5.LeadingDigitSeparator
    Public Module Separators
        ' VB 15 allowed the underscore only BETWEEN digits. VB 15.5 allows one
        ' immediately after the base prefix, so the prefix can be set off from
        ' the digits it introduces.
        Public Const LeadingBinary As Integer = &B_1010_1010

        Public Const LeadingHex As Integer = &H_FF_FF

        Public Const LeadingOctal As Integer = &O_7_7

        ' The VB 15 form remains legal; the separator is cosmetic either way.
        Public Const BetweenDigitsOnly As Integer = &B1010_1010

        Public Function FormsAreEqual() As Boolean
            Return LeadingBinary = BetweenDigitsOnly
        End Function

        ' A leading separator is only allowed after a BASE prefix — a plain
        ' decimal literal may not start with one.
        Public Function Mask(value As Integer) As Integer
            Return value And LeadingHex
        End Function
    End Module
End Namespace
