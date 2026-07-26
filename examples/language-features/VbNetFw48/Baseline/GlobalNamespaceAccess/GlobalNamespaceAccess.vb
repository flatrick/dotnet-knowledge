Option Strict On

Namespace Baseline.GlobalNamespaceAccess
    ' A local type deliberately named the same as a framework one, to create
    ' the ambiguity the Global keyword resolves.
    Public Class System
        Public Shared Function Name() As String
            Return "local"
        End Function
    End Class

    Public Module GlobalAccess
        ' Without qualification, the nearer System wins and this would find the
        ' class above. Global escapes to the root namespace, which is how a
        ' framework type stays reachable when a local name shadows it.
        Public Function FrameworkString(value As Integer) As String
            Return Global.System.Convert.ToString(value)
        End Function

        Public Function LocalType() As String
            Return System.Name()
        End Function

        ' Global also disambiguates a project's own root namespace when it
        ' collides with a nested one.
        Public Function FullyQualified() As Integer
            Return Global.System.Math.Abs(-3)
        End Function
    End Module
End Namespace
