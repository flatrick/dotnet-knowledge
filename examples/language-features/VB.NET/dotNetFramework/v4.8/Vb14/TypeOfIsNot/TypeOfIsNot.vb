Option Strict On

Namespace Vb14.TypeOfIsNot
    Public Module TypeTests
        ' TypeOf ... Is tests the runtime type. VB 14 added the negated form,
        ' so the test no longer has to be wrapped in Not.
        Public Function IsNotString(value As Object) As Boolean
            Return TypeOf value IsNot String
        End Function

        ' The pre-VB14 spelling, kept as contrast — note the extra parentheses
        ' the precedence required.
        Public Function IsNotStringLegacy(value As Object) As Boolean
            Return Not (TypeOf value Is String)
        End Function

        Public Function FormsAgree(value As Object) As Boolean
            Return IsNotString(value) = IsNotStringLegacy(value)
        End Function

        ' It reads best in a guard clause, which is what motivated it.
        Public Function Describe(value As Object) As String
            If TypeOf value IsNot Integer Then
                Return "not an integer"
            End If

            Return "integer"
        End Function
    End Module
End Namespace
