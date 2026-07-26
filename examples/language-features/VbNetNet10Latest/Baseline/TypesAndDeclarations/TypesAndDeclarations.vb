Option Strict On

Namespace Baseline.TypesAndDeclarations
    ' A Class is a reference type.
    Public Class Customer
        Public Property Name As String
    End Class

    ' A Structure is a value type.
    Public Structure Point
        Public X As Integer
        Public Y As Integer
    End Structure

    ' An Interface declares members without implementing them.
    Public Interface INamed
        ReadOnly Property Name As String
    End Interface

    ' A Module holds only shared members and needs no instance. It is the VB
    ' construct closest to a C# static class, and its members are accessible
    ' without qualification from the same namespace.
    Public Module Helpers
        Public Function Describe(value As Integer) As String
            Return "value=" & value.ToString()
        End Function
    End Module

    ' An Enum names a set of integral constants.
    Public Enum Level
        Low
        High
    End Enum

    ' Enums may state their underlying type and explicit values.
    Public Enum Status As Byte
        Unknown = 0
        Ready = 10
    End Enum
End Namespace
