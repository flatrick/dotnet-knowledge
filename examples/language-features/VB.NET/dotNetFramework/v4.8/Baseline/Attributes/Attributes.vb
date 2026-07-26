Option Strict On

Imports System

Namespace Baseline.Attributes
    ' Attributes are written in angle brackets rather than square ones, and an
    ' attribute applied to a declaration sits on the same logical line — which
    ' is why the line-continuation underscore appears here in older VB. Since
    ' VS2010 the continuation is usually implicit.
    <AttributeUsage(AttributeTargets.Class Or AttributeTargets.Method Or AttributeTargets.Parameter, AllowMultiple:=True)>
    Public Class AuditedAttribute
        Inherits Attribute

        Public Sub New(reason As String)
            Me.Reason = reason
        End Sub

        Public ReadOnly Property Reason As String

        ' A named argument is set with := at the use site.
        Public Property Severity As Integer
    End Class

    <Audited("class-level", Severity:=1)>
    Public Class Subject
        <Audited("method-level")>
        Public Function Work() As Integer
            Return 1
        End Function

        ' AllowMultiple:=True permits repetition.
        <Audited("first")>
        <Audited("second")>
        Public Function Twice() As Integer
            Return 2
        End Function

        ' An attribute on a parameter, and on the return value.
        Public Function Measure(<Audited("param")> value As Integer) As Integer
            Return value
        End Function
    End Class

    Public Module Reading
        Public Function ReasonOf() As String
            Dim attributes = GetType(Subject).GetCustomAttributes(GetType(AuditedAttribute), False)
            Return DirectCast(attributes(0), AuditedAttribute).Reason
        End Function
    End Module
End Namespace
