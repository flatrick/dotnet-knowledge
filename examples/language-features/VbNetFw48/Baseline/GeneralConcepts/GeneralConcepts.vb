Option Strict On

Namespace Baseline.GeneralConcepts
    Public Interface IShape
        Function Area() As Double
    End Interface

    ' MustInherit is VB's abstract; MustOverride marks a member with no body.
    Public MustInherit Class Shape
        Implements IShape

        Public MustOverride Function Area() As Double Implements IShape.Area

        ' Overridable is virtual; the derived type may replace it.
        Public Overridable Function Describe() As String
            Return "shape"
        End Function
    End Class

    Public Class Square
        Inherits Shape

        Private ReadOnly _side As Double

        Public Sub New(side As Double)
            _side = side
        End Sub

        Public Overrides Function Area() As Double
            Return _side * _side
        End Function

        ' MyBase reaches the base implementation, as C#'s base does.
        Public Overrides Function Describe() As String
            Return MyBase.Describe() & ":square"
        End Function
    End Class

    ' NotInheritable is sealed.
    Public NotInheritable Class UnitSquare
        Inherits Square

        Public Sub New()
            MyBase.New(1)
        End Sub
    End Class

    ' A generic type with a constraint.
    Public Class Box(Of T As {Structure})
        Private ReadOnly _value As T

        Public Sub New(value As T)
            _value = value
        End Sub

        Public ReadOnly Property Value As T
            Get
                Return _value
            End Get
        End Property
    End Class

    Public Module Polymorphism
        ' A generic method.
        Public Function FirstOrDefault(Of T)(values As T()) As T
            If values Is Nothing OrElse values.Length = 0 Then
                Return Nothing
            End If

            Return values(0)
        End Function

        ' Dispatch goes to the runtime type, not the declared one.
        Public Function AreaOf(shape As Shape) As Double
            Return shape.Area()
        End Function
    End Module
End Namespace
