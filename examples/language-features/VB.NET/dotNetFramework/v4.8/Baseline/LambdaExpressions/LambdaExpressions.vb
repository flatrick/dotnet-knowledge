Option Strict On
Option Infer On

Imports System
Imports System.Collections.Generic

Namespace Baseline.LambdaExpressions
    Public Module Lambdas
        ' A single-expression lambda uses Function with no End Function.
        Public Function Square() As Func(Of Integer, Integer)
            Return Function(value) value * value
        End Function

        ' A multi-line lambda needs the closing End Function, and is the form
        ' VS2010 added; VS2008 had only the expression form.
        Public Function Classify() As Func(Of Integer, String)
            Return Function(value)
                       If value < 0 Then
                           Return "negative"
                       End If

                       Return "non-negative"
                   End Function
        End Function

        ' A Sub lambda returns nothing — VB distinguishes the two where C# uses
        ' Action and Func to tell them apart.
        Public Function Recorder(sink As List(Of Integer)) As Action(Of Integer)
            Return Sub(value) sink.Add(value)
        End Function

        Public Function MultiLineSub(sink As List(Of Integer)) As Action(Of Integer)
            Return Sub(value)
                       sink.Add(value)
                       sink.Add(value * 2)
                   End Sub
        End Function

        ' Parameter types may be written out when inference is not enough.
        Public Function Add() As Func(Of Integer, Integer, Integer)
            Return Function(left As Integer, right As Integer) left + right
        End Function

        ' Captures work as in C#.
        Public Function Incrementer(start As Integer) As Func(Of Integer)
            Dim current As Integer = start
            Return Function() current + 1
        End Function
    End Module
End Namespace
