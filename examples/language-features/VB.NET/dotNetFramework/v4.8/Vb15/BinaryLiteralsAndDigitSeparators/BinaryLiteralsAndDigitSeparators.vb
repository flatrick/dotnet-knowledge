Option Strict On

Namespace Vb15.BinaryLiteralsAndDigitSeparators
    Public Module Literals
        ' VB has had &H for hexadecimal and &O for octal since 1.0. VB 15 added
        ' &B for binary, and the underscore digit separator.
        Public Const AlternatingBits As Integer = &B1010_1010

        Public Const LowNibbleMask As Integer = &B0000_1111

        ' Separators work in the other bases and in decimal too.
        Public Const HexMask As Integer = &HFF_FF

        Public Const Octal As Integer = &O7_7

        Public Const Million As Integer = 1_000_000

        ' Grouping carries no meaning of its own.
        Public Function SeparatorsAreCosmetic() As Boolean
            Return 1_0_0 = 100
        End Function

        Public Function Mask(value As Integer) As Integer
            Return value And LowNibbleMask
        End Function

        ' VB 15 required the separator to sit BETWEEN digits; the leading form
        ' arrived in 15.5 and has its own row.
        Public Function BetweenDigitsOnly() As Boolean
            Return AlternatingBits = 170
        End Function
    End Module
End Namespace
