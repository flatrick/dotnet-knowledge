' VB has no `dynamic` keyword. Its equivalent is LATE BINDING, which is enabled
' by Option Strict Off — the reason this file states the option explicitly
' rather than relying on the project default.
Option Strict Off
Option Infer On

Imports System.Dynamic

Namespace Baseline.DynamicBinding
    Public Class Greeter
        Public Function Greet(name As String) As String
            Return "hello " & name
        End Function
    End Class

    ' A DynamicObject participates in the DLR, and VB's late binder honors it —
    ' which is what VS2010 added: late binding was extended to understand
    ' IDynamicMetaObjectProvider rather than only reflection.
    Public Class Bag
        Inherits DynamicObject

        Private ReadOnly _values As New Dictionary(Of String, Object)()

        Public Overrides Function TrySetMember(binder As SetMemberBinder, value As Object) As Boolean
            _values(binder.Name) = value
            Return True
        End Function

        Public Overrides Function TryGetMember(binder As GetMemberBinder, ByRef result As Object) As Boolean
            Return _values.TryGetValue(binder.Name, result)
        End Function
    End Class

    Public Module LateBinding
        ' The member is resolved at run time, not compile time. Under Option
        ' Strict On this line would not compile.
        Public Function CallResolvedAtRuntime() As String
            Dim greeter As Object = New Greeter()
            Return greeter.Greet("world")
        End Function

        ' Against a DynamicObject the same syntax goes through the DLR instead
        ' of reflection.
        Public Function ThroughDynamicObject() As Object
            Dim bag As Object = New Bag()
            bag.Anything = 42
            Return bag.Anything
        End Function

        ' Operators are late-bound too.
        Public Function AddDynamically(left As Object, right As Object) As Object
            Return left + right
        End Function
    End Module
End Namespace
