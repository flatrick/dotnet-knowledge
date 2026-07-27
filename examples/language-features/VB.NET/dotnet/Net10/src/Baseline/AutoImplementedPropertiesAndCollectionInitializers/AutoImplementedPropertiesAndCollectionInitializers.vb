Option Strict On

Imports System.Collections.Generic

Namespace Baseline.AutoImplementedPropertiesAndCollectionInitializers
    Public Class Customer
        ' An auto-implemented property generates its backing field. VB names
        ' that field predictably as an underscore followed by the property name,
        ' so _Name is reachable from inside the type — unlike C#, where the
        ' generated field has an unspeakable name.
        Public Property Name As String

        Public Property Age As Integer

        ' An auto-property may carry an initializer.
        Public Property Region As String = "global"

        ' A read-only value assigned from the constructor, written the pre-VB14
        ' way: an explicit backing field behind a ReadOnly property. A ReadOnly
        ' *auto*-implemented property is VB 14, above this row's era.
        Private ReadOnly _id As String

        Public ReadOnly Property Id As String
            Get
                Return _id
            End Get
        End Property

        Public Sub New(id As String)
            _id = id
            Name = String.Empty
        End Sub

        ' Demonstrating the predictable backing-field name.
        Public Function NameFieldLength() As Integer
            Return If(_Name, String.Empty).Length
        End Function
    End Class

    Public Module Initializers
        ' A collection initializer uses From. Each element is passed to Add.
        Public Function Numbers() As List(Of Integer)
            Return New List(Of Integer) From {1, 2, 3}
        End Function

        ' A Dictionary's Add takes two arguments, so each element is a nested
        ' brace pair.
        Public Function Lookup() As Dictionary(Of String, Integer)
            Return New Dictionary(Of String, Integer) From {{"one", 1}, {"two", 2}}
        End Function

        ' From and With combine: the collection is initialized with objects
        ' that are themselves initialized.
        Public Function Customers() As List(Of Customer)
            Return New List(Of Customer) From {
                New Customer("a") With {.Name = "Ada", .Age = 36},
                New Customer("b") With {.Name = "Grace", .Age = 45}
            }
        End Function
    End Module
End Namespace
