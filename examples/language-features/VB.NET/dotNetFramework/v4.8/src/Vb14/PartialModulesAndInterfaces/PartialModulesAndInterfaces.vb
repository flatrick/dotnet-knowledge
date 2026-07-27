Option Strict On

Namespace Vb14.PartialModulesAndInterfaces
    ' Partial had applied only to classes and structures. VB 14 extended it to
    ' modules and interfaces, so a generator can contribute to either.
    Partial Public Module Helpers
        Public Function First() As Integer
            Return 1
        End Function
    End Module

    Partial Public Module Helpers
        Public Function Second() As Integer
            Return 2
        End Function
    End Module

    Partial Public Interface IService
        Function Read() As String
    End Interface

    Partial Public Interface IService
        Function Write(value As String) As Boolean
    End Interface

    Public Class Service
        Implements IService

        Public Function Read() As String Implements IService.Read
            Return "value"
        End Function

        Public Function Write(value As String) As Boolean Implements IService.Write
            Return value IsNot Nothing
        End Function
    End Class

    Public Module Usage
        ' Both halves of the module are one module at the call site.
        Public Function Total() As Integer
            Return Helpers.First() + Helpers.Second()
        End Function

        ' ...and both halves of the interface are one contract.
        Public Function UseService() As String
            Dim service As IService = New Service()
            Return If(service.Write("x"), service.Read(), String.Empty)
        End Function
    End Module
End Namespace
