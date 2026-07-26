Option Strict On

Namespace Baseline.Conversions
    Public Class Celsius
        Private ReadOnly _degrees As Double

        Public Sub New(degrees As Double)
            _degrees = degrees
        End Sub

        Public ReadOnly Property Degrees As Double
            Get
                Return _degrees
            End Get
        End Property

        ' A widening conversion never loses information, so it may be implicit.
        Public Shared Widening Operator CType(value As Celsius) As Double
            Return value._degrees
        End Operator

        ' A narrowing conversion may lose information, so under Option Strict On
        ' the caller must ask for it explicitly with CType.
        Public Shared Narrowing Operator CType(value As Celsius) As Integer
            Return CInt(value._degrees)
        End Operator
    End Class

    Public Module ConversionSamples
        ' Widening: Integer to Long loses nothing, so no cast is needed even
        ' under Option Strict On.
        Public Function Widen(value As Integer) As Long
            Return value
        End Function

        ' Narrowing: Double to Integer may lose the fraction, so it must be
        ' requested. CInt rounds; CType and DirectCast do not convert numerics.
        Public Function Narrow(value As Double) As Integer
            Return CInt(value)
        End Function

        ' DirectCast requires the runtime type to match exactly; it does no
        ' conversion, which makes it the cheapest and the strictest.
        Public Function Unbox(value As Object) As Integer
            Return DirectCast(value, Integer)
        End Function

        ' TryCast returns Nothing instead of throwing when the cast fails, and
        ' works only on reference types.
        Public Function AsStringOrNothing(value As Object) As String
            Return TryCast(value, String)
        End Function

        Public Function UseUserDefined() As Double
            Dim temperature As New Celsius(21.5)
            Return temperature
        End Function

        Public Function UseNarrowingUserDefined() As Integer
            Dim temperature As New Celsius(21.5)
            Return CType(temperature, Integer)
        End Function
    End Module
End Namespace
