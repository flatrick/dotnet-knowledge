Option Strict On

Namespace Vb14.OverridesImplicitlyOverloads
    Public MustInherit Class Repository
        Public Overridable Function Find(id As Integer) As String
            Return "base:" & id.ToString()
        End Function

        Public Overridable Function Find(name As String) As String
            Return "base:" & name
        End Function
    End Class

    ' Before VB 14, overriding one member of an overload set required the
    ' Overloads modifier as well, or the override hid its siblings. VB 14 makes
    ' Overrides imply Overloads, so overriding one leaves the rest reachable.
    Public Class SqlRepository
        Inherits Repository

        Public Overrides Function Find(id As Integer) As String
            Return "sql:" & id.ToString()
        End Function
    End Class

    ' Writing both modifiers remains legal and means the same thing.
    Public Class CachedRepository
        Inherits Repository

        Public Overloads Overrides Function Find(id As Integer) As String
            Return "cached:" & id.ToString()
        End Function
    End Class

    Public Module Usage
        ' The String overload is inherited and still callable, which is the
        ' behavior the change guarantees.
        Public Function InheritedOverloadStillVisible() As String
            Dim repository As New SqlRepository()
            Return repository.Find("byName")
        End Function

        Public Function OverriddenOverload() As String
            Dim repository As New SqlRepository()
            Return repository.Find(1)
        End Function
    End Module
End Namespace
