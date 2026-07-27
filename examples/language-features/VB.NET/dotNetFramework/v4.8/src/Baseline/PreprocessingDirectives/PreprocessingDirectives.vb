Option Strict On

' #Const declares a compile-time constant, visible only to the preprocessor.
#Const SampleFlag = True
#Const SampleLevel = 2

Namespace Baseline.PreprocessingDirectives
    Public Module Directives
        Public Function Conditional() As String
            ' VB's conditional compilation uses #If/#ElseIf/#Else/#End If, and
            ' unlike C# it evaluates full constant expressions rather than only
            ' symbol presence.
#If SampleFlag AndAlso SampleLevel >= 2 Then
            Return "high"
#ElseIf SampleFlag Then
            Return "low"
#Else
            Return "off"
#End If
        End Function

        ' The compiler defines symbols for the target framework, so a file can
        ' adapt without a project change.
        Public Function TargetSpecific() As String
#If NET Then
            Return "net"
#Else
            Return "other"
#End If
        End Function

        ' #Region collapses a block in an editor and has no effect on the build.
#Region "Helpers"
        Public Function Helper() As Integer
            Return 1
        End Function
#End Region

        ' #ExternalSource maps a span to another file, so diagnostics and
        ' debugger steps land in the original rather than in generated text.
        ' It is VB's counterpart to C#'s #line.
        Public Function Generated() As Integer
#ExternalSource ("Original.template", 7)
            Return 42
#End ExternalSource
        End Function
    End Module
End Namespace
