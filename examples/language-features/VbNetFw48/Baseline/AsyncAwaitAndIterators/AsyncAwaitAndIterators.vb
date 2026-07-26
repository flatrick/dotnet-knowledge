Option Strict On

Imports System.Collections.Generic
Imports System.Threading.Tasks

Namespace Baseline.AsyncAwaitAndIterators
    Public Module AsyncSamples
        ' Async and Await are modifiers and operators rather than contextual
        ' keywords in a signature position, but behave as C#'s do.
        Public Async Function AddAsync(left As Integer, right As Integer) As Task(Of Integer)
            Await Task.Yield()
            Return left + right
        End Function

        ' A Sub cannot be awaited, so Async Sub is VB's async void — for event
        ' handlers only.
        Public Async Sub FireAndForget(sink As List(Of Integer))
            Await Task.Yield()
            sink.Add(1)
        End Sub

        Public Async Function SumConcurrentlyAsync() As Task(Of Integer)
            Dim first As Task(Of Integer) = AddAsync(1, 2)
            Dim second As Task(Of Integer) = AddAsync(3, 4)
            Dim results As Integer() = Await Task.WhenAll(first, second)
            Return results(0) + results(1)
        End Function
    End Module

    Public Module IteratorSamples
        ' VB requires the Iterator modifier on the method, which C# infers from
        ' the presence of yield. Yield is a statement here, not a contextual
        ' keyword pair.
        Public Iterator Function Range(count As Integer) As IEnumerable(Of Integer)
            For i As Integer = 0 To count - 1
                Yield i
            Next
        End Function

        ' An iterator may also be written as a lambda.
        Public Function Squares(count As Integer) As IEnumerable(Of Integer)
            Dim generator = Iterator Function() As IEnumerable(Of Integer)
                                For i As Integer = 1 To count
                                    Yield i * i
                                Next
                            End Function

            Return generator()
        End Function

        ' Note the local's name: in VB a function's own name doubles as an
        ' implicit return variable, so a local may not shadow it (BC30290).
        ' `Dim total` inside `Function Total` is therefore an error, which has
        ' no C# counterpart.
        Public Function Total(count As Integer) As Integer
            Dim running As Integer = 0

            For Each value As Integer In Range(count)
                running += value
            Next

            Return running
        End Function
    End Module
End Namespace
