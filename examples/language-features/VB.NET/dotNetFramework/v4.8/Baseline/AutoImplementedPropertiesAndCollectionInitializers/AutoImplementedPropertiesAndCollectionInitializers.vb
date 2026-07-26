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

        ' ...and a ReadOnly one may still be assigned from a constructor.
        Public ReadOnly Property Id As String

        Public Sub New(id As String)
            _Id = id
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
