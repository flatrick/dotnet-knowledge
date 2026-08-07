Imports System
Imports Vb15.ConsumingCSharpRefReturnValues

Module Program
    Sub Main()
        Console.WriteLine(RefReturns.ReadThroughRefReturn())
        Console.WriteLine(RefReturns.MutateThroughApi())
    End Sub
End Module
