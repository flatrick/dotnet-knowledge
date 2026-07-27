Option Strict On

Imports System

Namespace Vb14.NameOfOperator
    Public Class Account
        Private ReadOnly _owner As String

        Public ReadOnly Property Owner As String
            Get
                Return _owner
            End Get
        End Property

        ' NameOf produces the name as a compile-time constant, so renaming the
        ' parameter updates this string too. VB spells it NameOf; C# spells it
        ' nameof — the same operator, arriving in both languages together.
        Public Sub New(owner As String)
            If owner Is Nothing Then
                Throw New ArgumentNullException(NameOf(owner))
            End If

            _owner = owner
        End Sub

        ' Only the final identifier is produced, not the qualified path.
        Public Shared Function MemberName() As String
            Return NameOf(Account.Owner)
        End Function

        Public Shared Function TypeName() As String
            Return NameOf(Account)
        End Function

        Public Shared Function LocalName() As String
            Dim total As Integer = 0
            Return NameOf(total) & total.ToString()
        End Function
    End Class
End Namespace
