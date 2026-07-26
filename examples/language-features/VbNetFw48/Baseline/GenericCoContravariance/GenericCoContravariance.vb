Option Strict On

Imports System.Collections.Generic

Namespace Baseline.GenericCoContravariance
    Public Class Animal
    End Class

    Public Class Dog
        Inherits Animal
    End Class

    ' Out marks T covariant — it may appear only in output positions. VB spells
    ' the annotations Out and In, matching C#'s out and in.
    Public Interface IProducer(Of Out T)
        Function Produce() As T
    End Interface

    ' In marks T contravariant: input positions only.
    Public Interface IConsumer(Of In T)
        Function Accepts(item As T) As Boolean
    End Interface

    Public Delegate Function Factory(Of Out T)() As T

    Public Delegate Sub Handler(Of In T)(item As T)

    Public Class DogProducer
        Implements IProducer(Of Dog)

        Public Function Produce() As Dog Implements IProducer(Of Dog).Produce
            Return New Dog()
        End Function
    End Class

    Public Class AnimalConsumer
        Implements IConsumer(Of Animal)

        Public Function Accepts(item As Animal) As Boolean Implements IConsumer(Of Animal).Accepts
            Return item IsNot Nothing
        End Function
    End Class

    Public Module Variance
        ' Covariance: an IProducer(Of Dog) is usable where IProducer(Of Animal)
        ' is expected, because T only ever comes out.
        Public Function Covariant() As IProducer(Of Animal)
            Dim dogs As IProducer(Of Dog) = New DogProducer()
            Return dogs
        End Function

        ' Contravariance: an IConsumer(Of Animal) is usable where
        ' IConsumer(Of Dog) is expected, because T only ever goes in.
        Public Function Contravariant() As IConsumer(Of Dog)
            Dim animals As IConsumer(Of Animal) = New AnimalConsumer()
            Return animals
        End Function

        ' The BCL carries the same annotations, which is what makes this legal.
        Public Function BclCovariance() As IEnumerable(Of Animal)
            Dim dogs As New List(Of Dog)()
            dogs.Add(New Dog())
            Return dogs
        End Function
    End Module
End Namespace
