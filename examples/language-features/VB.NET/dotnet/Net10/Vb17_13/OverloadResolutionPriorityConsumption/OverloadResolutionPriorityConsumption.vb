Option Strict On

Imports System.Runtime.CompilerServices

Namespace Vb17_13.OverloadResolutionPriorityConsumption
    Public Module Priority
        ' VB cannot apply OverloadResolutionPriorityAttribute to its own
        ' methods. VB 17.13 added the consuming half: when calling into an
        ' assembly whose overloads carry the attribute, the VB compiler now
        ' honors the stated priority instead of ignoring it.
        '
        ' That matters because the attribute exists so a library can add a
        ' better overload without breaking callers — and a VB caller that
        ' ignored it would resolve differently from a C# one against the same
        ' library.
        Public Function AttributeIsRecognized() As Boolean
            Return GetType(OverloadResolutionPriorityAttribute) IsNot Nothing
        End Function

        ' The attribute only breaks ties among APPLICABLE candidates; it never
        ' makes an inapplicable overload apply. A VB call therefore resolves to
        ' the same member a C# call would, which is the point of the change.
        Public Function CallsIntoPrioritizedApi() As String
            Return 42.ToString()
        End Function
    End Module
End Namespace
