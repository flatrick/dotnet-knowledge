Option Strict On

Imports System

Namespace Vb14.SmarterNameResolution
    ' A namespace whose leading segment collides with a type name in scope.
    Namespace Threading
        Public Class Worker
            Public Shared Function Run() As String
                Return "local"
            End Function
        End Class
    End Namespace

    Public Module Resolution
        ' Before VB 14 a fully-qualified name was resolved one segment at a
        ' time, so an intermediate segment matching something nearer could
        ' derail it. VB 14 considers the whole qualified name, so this reaches
        ' the framework type even though a nearer Threading namespace exists.
        Public Function FrameworkType() As Type
            Return GetType(System.Threading.Tasks.Task)
        End Function

        ' The local namespace remains reachable by its own path.
        Public Function LocalType() As String
            Return Threading.Worker.Run()
        End Function

        ' Global still forces the root, and is the explicit escape when a name
        ' is genuinely ambiguous.
        Public Function ExplicitlyGlobal() As Type
            Return GetType(Global.System.Threading.Tasks.Task)
        End Function
    End Module
End Namespace
