Option Strict On

Imports System.Runtime.CompilerServices

Namespace Baseline.CallerInfoAttributes
    Public Module Diagnostics
        ' The compiler fills these optional parameters in at each call site, so
        ' the caller writes nothing and no run-time stack inspection is needed.
        Public Function Describe(message As String,
                                 <CallerMemberName> Optional member As String = "",
                                 <CallerFilePath> Optional file As String = "",
                                 <CallerLineNumber> Optional line As Integer = 0) As String
            Return message & " (" & member & " in " & file & ":" & line.ToString() & ")"
        End Function

        Public Function CalledFromHere() As String
            ' member becomes "CalledFromHere"; file and line describe this call.
            Return Describe("checkpoint")
        End Function

        ' An explicit argument wins over the value the compiler would inject.
        Public Function CalledWithExplicitMember() As String
            Return Describe("checkpoint", "supplied-by-hand")
        End Function
    End Module
End Namespace
