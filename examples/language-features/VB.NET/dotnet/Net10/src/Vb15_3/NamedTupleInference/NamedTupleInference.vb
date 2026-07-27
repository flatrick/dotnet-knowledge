Option Strict On
Option Infer On

Namespace Vb15_3.NamedTupleInference
    Public Class Person
        Public Property Name As String
        Public Property Age As Integer
    End Class

    Public Module Inference
        ' VB 15 required every tuple element name to be written out. VB 15.3
        ' infers them from the expressions, exactly as C# 7.1 does.
        Public Function FromLocals(name As String, count As Integer) As String
            Dim pair = (name, count)
            Return pair.name & ":" & pair.count.ToString()
        End Function

        ' A property access infers the property's name.
        Public Function FromProperties(person As Person) As Integer
            Dim projected = (person.Name, person.Age)
            Return projected.Age + projected.Name.Length
        End Function

        ' An explicit name still wins over the inferred one.
        Public Function ExplicitWins(count As Integer) As Integer
            Dim pair = (total:=count, count)
            Return pair.total + pair.count
        End Function
    End Module
End Namespace
