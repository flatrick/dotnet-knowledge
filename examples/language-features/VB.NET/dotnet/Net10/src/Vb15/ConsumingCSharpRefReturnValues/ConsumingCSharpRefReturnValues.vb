Option Strict On

Imports System
Imports System.Collections.Generic
Imports System.Runtime.InteropServices

Namespace Vb15.ConsumingCSharpRefReturnValues
    Public Module RefReturns
        ' VB cannot DECLARE a ref-returning method or a ref local. VB 15 added
        ' the other half: it can CALL a method that returns a reference, and
        ' reads the value through it. VB copies the value out at the call site,
        ' because it has nowhere to store the reference itself.
        '
        ' CollectionsMarshal.GetValueRefOrNullRef is such a BCL method, and it
        ' involves no ref struct — so this is the form that needs no caveat.
        Public Function ReadThroughRefReturn() As Integer
            Dim map As New Dictionary(Of String, Integer)()
            map("a") = 41

            Return CollectionsMarshal.GetValueRefOrNullRef(map, "a") + 1
        End Function

        Public Function MutateThroughApi() As Integer
            Dim map As New Dictionary(Of String, Integer)()
            map("a") = 1
            map("a") = map("a") + 1
            Return map("a")
        End Function
    End Module

    ' Span(Of T) is normally unusable from VB: the compiler reports BC30668,
    ' "types with embedded references are not supported in this version of your
    ' compiler", because VB has no ref struct.
    '
    ' That diagnostic is an Obsolete attribute the BCL puts on Span for exactly
    ' this purpose, and a member that is ITSELF marked Obsolete suppresses
    ' obsolete diagnostics inside it. So the attribute below does not add ref
    ' struct support — it removes the compiler's only way of saying it has none.
    '
    ' What that buys, verified by running each: indexing works, a Span really is
    ' a VIEW so writing through it reaches the backing array, and a
    ' ref-returning API over a Span is consumable.
    '
    ' What it costs: VB performs no ref-safety analysis, so it will emit IL the
    ' runtime rejects. Boxing a Span, or storing one in a field, compiles here
    ' and then fails at JIT time with InvalidProgramException — not a catchable
    ' domain error, but invalid IL. C# refuses both at compile time; VB cannot.
    ' Treat this as a way to CONSUME a Span in a narrow, local scope, never as
    ' ref struct support.
    Public Module SpanConsumption
        <Obsolete("Suppresses BC30668 so a Span may be consumed; see the notes above.", False)>
        Public Function ReadElement() As Integer
            Dim values As New Span(Of Integer)({1, 2, 3})
            Return values(1)
        End Function

        <Obsolete("Suppresses BC30668 so a Span may be consumed; see the notes above.", False)>
        Public Function WriteThroughView() As Integer
            Dim backing() As Integer = {1, 2, 3}
            Dim values As New Span(Of Integer)(backing)
            values(0) = 42

            ' The write reached the array, which is what makes it a view.
            Return backing(0)
        End Function

        <Obsolete("Suppresses BC30668 so a Span may be consumed; see the notes above.", False)>
        Public Function RefReturnOverSpan() As Integer
            Dim values As New Span(Of Integer)({7, 8, 9})

            ' A genuine ref return, consumed by value as VB 15 allows.
            Return MemoryMarshal.GetReference(values)
        End Function
    End Module
End Namespace
