Option Strict On
Option Infer On

Imports System.Collections.Generic

Namespace Baseline.LocalTypeInferenceAndObjectInitializers
    Public Class Point
        Public Property X As Integer
        Public Property Y As Integer
    End Class

    Public Module Inference
        ' With Option Infer On a Dim takes its type from the initializer, as
        ' C#'s var does. With Infer Off the same declaration would be Object,
        ' which is why the file states the option explicitly.
        Public Function Inferred() As Integer
            Dim count = 42
            Return count
        End Function

        Public Function InferredCollection() As Integer
            Dim lookup = New Dictionary(Of String, List(Of Integer))()
            lookup.Add("first", New List(Of Integer)())
            Return lookup.Count
        End Function

        ' An object initializer uses With and dotted member names.
        Public Function ObjectInitializer() As Point
            Return New Point With {.X = 3, .Y = 4}
        End Function

        ' Nested initializers work the same way.
        Public Function CollectionOfInitialized() As List(Of Point)
            Return New List(Of Point) From {
                New Point With {.X = 1, .Y = 2},
                New Point With {.X = 3, .Y = 4}
            }
        End Function

        ' A collection initializer uses From rather than With.
        Public Function CollectionInitializer() As List(Of Integer)
            Return New List(Of Integer) From {1, 2, 3}
        End Function
    End Module
End Namespace
