Option Strict On

Namespace Vb15_5.PrivateProtectedAccessModifier
    Public Class Repository
        ' Private Protected is the INTERSECTION: derived types, but only those
        ' in this assembly. VB spells it with the two words in that order,
        ' matching C#'s private protected.
        Private Protected ConnectionCount As Integer

        ' Protected Friend is the UNION instead: derived types anywhere, OR any
        ' type in this assembly. Friend is VB's spelling of internal, so this
        ' is C#'s protected internal.
        Protected Friend RetryCount As Integer

        Public Sub New()
            ConnectionCount = 1
            RetryCount = 3
        End Sub
    End Class

    ' Derived AND in the same assembly, so it reaches both.
    Public Class SqlRepository
        Inherits Repository

        Public Function Total() As Integer
            Return ConnectionCount + RetryCount
        End Function
    End Class
End Namespace
