' Option statements are FILE-scoped in VB and override the project default for
' this file alone. That is a real difference from C#, where the comparable
' switches (/unsafe, OutputType) are per-compilation and cannot be narrowed —
' which is why the C# half of this corpus needs extra projects and VB does not.
'
' This project's defaults, as passed by the SDK, are:
'   /optionexplicit+   /optioninfer+   /optioncompare:  (Binary)
'   /optionstrict:custom
' so the two statements below tighten Strict and leave the rest as they are.
Option Strict On
Option Explicit On
Option Infer On
Option Compare Binary

Imports System.Text
Imports SB = System.Text.StringBuilder

Namespace Baseline.ImportsAndNamespaces
    Public Class Namespaces
        ' Imports brings a namespace into scope for the whole file.
        Public Shared Function Build(value As String) As String
            Dim builder As New StringBuilder()
            builder.Append(value)
            Return builder.ToString()
        End Function

        ' An Imports alias names a single type.
        Public Shared Function ViaAlias(value As String) As Integer
            Dim builder As New SB(value)
            Return builder.Length
        End Function

        ' Option Strict On forbids implicit narrowing and late binding, so the
        ' conversion here has to be written out.
        Public Shared Function ExplicitNarrowing(value As Double) As Integer
            Return CInt(value)
        End Function

        ' Option Infer On lets Dim take its type from the initializer, the way
        ' C#'s var does. With Infer Off this would be Object.
        Public Shared Function Inferred() As Integer
            Dim count = 3
            Return count
        End Function

        ' Option Compare Binary makes string comparison ordinal, so case
        ' matters. Option Compare Text would make this True.
        Public Shared Function CaseSensitiveComparison() As Boolean
            Return "ABC" = "abc"
        End Function
    End Class
End Namespace
