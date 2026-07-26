Option Strict On

Namespace Vb14.DisableEnableWarningDirectives
    Public Module Warnings
        ' #Disable Warning suppresses a diagnostic from that point on, and
        ' #Enable Warning restores it. Naming the code keeps the suppression
        ' narrow — the directives with no code affect every warning, which is
        ' almost never what is wanted.
        '
        ' This matters in a tree that treats warnings as errors: the directive
        ' is the supported way to make a deliberate exception visible in the
        ' source rather than hidden in a project setting.
        Public Function Unused() As Integer
#Disable Warning BC42024
            Dim neverRead As Integer
#Enable Warning BC42024
            Return 0
        End Function

        ' Outside the disabled span the warning is live again, so this method
        ' avoids the pattern rather than suppressing it.
        Public Function Used() As Integer
            Dim value As Integer = 1
            Return value
        End Function
    End Module
End Namespace
