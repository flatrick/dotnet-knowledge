Option Strict On

Imports System
Imports System.Runtime.InteropServices
Imports CSharpFw48Cs80.CSharp7.RefReturnsAndLocals

Namespace Vb15.ConsumingCSharpRefReturnValues
    Public Module RefReturns
        ' VB cannot DECLARE a ref-returning method or a ref local. VB 15 added
        ' the other half: it can CALL a method that returns a reference, and
        ' reads the value through it. VB copies the value out at the call site,
        ' because it has nowhere to store the reference itself.
        '
        ' The subject here is RefSamples.Find, the C# ref-returning method this
        ' corpus authors for its own C# 7.0 row. net10's version of this sample
        ' uses CollectionsMarshal.GetValueRefOrNullRef instead, which net48 has
        ' no backport for; consuming the corpus's own C# assembly is the closer
        ' fit to what the row is named for in any case. Neither form involves a
        ' ref struct, so neither needs the suppression the module below does.
        Public Function ReadThroughRefReturn() As Integer
            Dim values() As Integer = {10, 41, 30}

            Return RefSamples.Find(values, 41) + 1
        End Function

        ' The reference is real on the C# side — ReplaceInPlace assigns through
        ' it and the array changes. VB reads the value out rather than holding
        ' the alias, so the write has to happen where the ref local can live.
        Public Function MutateThroughApi() As Integer
            Dim values() As Integer = {1, 2, 3}
            RefSamples.ReplaceInPlace(values, 2, 99)
            Return values(1)
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
    '
    ' On net48 Span arrives through the System.Memory package rather than the
    ' shared framework. That changes where the type comes from, not how VB
    ' treats it: the Obsolete marker travels with the type, so the suppression
    ' and its hazard are identical to the net10 form of this row.
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
