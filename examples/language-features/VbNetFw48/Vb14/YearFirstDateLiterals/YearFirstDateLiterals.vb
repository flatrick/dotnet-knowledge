Option Strict On

Imports System

Namespace Vb14.YearFirstDateLiterals
    Public Module DateLiterals
        ' A date literal is delimited by # signs. VB 14 added the ISO-style
        ' year-first form, which is unambiguous; the older month-first form
        ' reads differently depending on where you are from.
        Public Function YearFirst() As Date
            Return #2026-07-26#
        End Function

        ' The original form remains legal and means the same date, but states
        ' the month first — the reason the year-first form was added.
        Public Function MonthFirst() As Date
            Return #7/26/2026#
        End Function

        Public Function SameDate() As Boolean
            Return YearFirst() = MonthFirst()
        End Function

        ' A time may be included in either form.
        Public Function WithTime() As Date
            Return #2026-07-26 14:30:00#
        End Function

        Public Function TimeOnly() As Date
            Return #14:30:00#
        End Function
    End Module
End Namespace
