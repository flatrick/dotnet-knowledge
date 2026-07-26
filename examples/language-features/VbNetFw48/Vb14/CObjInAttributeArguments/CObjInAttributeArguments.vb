Option Strict On

Imports System

Namespace Vb14.CObjInAttributeArguments
    <AttributeUsage(AttributeTargets.Class Or AttributeTargets.Method)>
    Public Class MetadataAttribute
        Inherits Attribute

        ' The parameter is Object, so any constant may be passed — but the
        ' compiler must be told which type to store.
        Public Sub New(value As Object)
            Me.Value = value
        End Sub

        Public ReadOnly Property Value As Object
    End Class

    ' VB 14 permits CObj in an attribute argument, which is how a value gets
    ' boxed into an Object-typed attribute parameter with its type preserved.
    ' Earlier versions rejected the conversion in this position.
    <Metadata(CObj(1))>
    Public Class TaggedWithInteger
    End Class

    <Metadata(CObj("text"))>
    Public Class TaggedWithString
    End Class

    Public Module Usage
        Public Function ReadInteger() As Object
            Dim attributes = GetType(TaggedWithInteger).GetCustomAttributes(GetType(MetadataAttribute), False)
            Return DirectCast(attributes(0), MetadataAttribute).Value
        End Function

        ' The stored value keeps the type CObj boxed, so this is an Integer
        ' rather than a String.
        Public Function StoredTypeIsInteger() As Boolean
            Return TypeOf ReadInteger() Is Integer
        End Function
    End Module
End Namespace
