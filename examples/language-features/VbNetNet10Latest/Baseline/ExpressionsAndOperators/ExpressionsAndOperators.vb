Option Strict On

Namespace Baseline.ExpressionsAndOperators
    Public Module Operators
        ' VB spells several operators as words, and separates integer from
        ' floating division: / always produces a floating result, \ truncates.
        Public Function Divide(left As Integer, right As Integer) As Double
            Return left / right
        End Function

        Public Function IntegerDivide(left As Integer, right As Integer) As Integer
            Return left \ right
        End Function

        Public Function Remainder(left As Integer, right As Integer) As Integer
            Return left Mod right
        End Function

        ' ^ is exponentiation, which C# has no operator for.
        Public Function Power(value As Double, exponent As Double) As Double
            Return value ^ exponent
        End Function

        ' & concatenates strings; + would also add numerics, so & states intent.
        Public Function Concatenate(left As String, right As String) As String
            Return left & right
        End Function

        ' AndAlso/OrElse short-circuit; And/Or do not and also serve as bitwise
        ' operators on integrals. Choosing the wrong pair is a classic VB bug.
        Public Function ShortCircuits(value As String) As Boolean
            Return value IsNot Nothing AndAlso value.Length > 0
        End Function

        Public Function Bitwise(left As Integer, right As Integer) As Integer
            Return left And right
        End Function

        ' Is and IsNot compare reference identity; = compares values.
        Public Function SameInstance(left As Object, right As Object) As Boolean
            Return left Is right
        End Function

        ' Precedence: ^ binds tighter than *, which binds tighter than +, so
        ' this is 2 + (3 * (4 ^ 2)) = 50.
        Public Function Precedence() As Double
            Return 2 + 3 * 4 ^ 2
        End Function

        ' Like performs pattern matching on strings, another VB-only operator.
        Public Function Matches(value As String) As Boolean
            Return value Like "A?C*"
        End Function
    End Module
End Namespace
