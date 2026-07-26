Option Strict On

Namespace Vb14.RegionDirectivesInsideMethodBodies
    Public Module Regions
        ' Before VB 14 a #Region had to begin and end outside any method body.
        ' Now it may sit inside one, and may also straddle a method boundary,
        ' which is what generators emitting partial bodies needed.
        Public Function Compute(values As Integer()) As Integer
            Dim total As Integer = 0

#Region "Accumulate"
            For Each value As Integer In values
                total += value
            Next
#End Region

#Region "Normalize"
            If total < 0 Then
                total = 0
            End If
#End Region

            Return total
        End Function

        ' Regions have no effect on the build; they only collapse in an editor.
        Public Function Unaffected() As Integer
            Return 1
        End Function
    End Module
End Namespace
