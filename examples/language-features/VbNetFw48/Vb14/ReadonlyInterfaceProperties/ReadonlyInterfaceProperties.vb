Option Strict On

Namespace Vb14.ReadonlyInterfaceProperties
    Public Interface INamed
        ' The interface requires only a getter.
        ReadOnly Property Name As String
    End Interface

    ' Before VB 14 the implementing property had to match the interface's
    ' ReadOnly-ness exactly. Now a read-write property may implement a
    ' ReadOnly one, since it satisfies everything the interface asks for.
    Public Class Person
        Implements INamed

        Public Property Name As String Implements INamed.Name
    End Class

    ' Implementing it as ReadOnly remains correct, and is what earlier versions
    ' required.
    Public Class Constant
        Implements INamed

        Public ReadOnly Property Name As String Implements INamed.Name
            Get
                Return "constant"
            End Get
        End Property
    End Class

    Public Module Usage
        Public Function ReadThroughInterface(named As INamed) As String
            Return named.Name
        End Function

        Public Function WriteThroughClass() As String
            Dim person As New Person()
            person.Name = "Ada"
            Return ReadThroughInterface(person)
        End Function
    End Module
End Namespace
