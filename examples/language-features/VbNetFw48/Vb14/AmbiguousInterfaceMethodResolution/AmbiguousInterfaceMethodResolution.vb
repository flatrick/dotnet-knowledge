Option Strict On

Namespace Vb14.AmbiguousInterfaceMethodResolution
    Public Interface IReader
        Function Read() As String
    End Interface

    Public Interface IWriter
        Function Read() As String
    End Interface

    ' Both interfaces declare Read. A single implementing member may satisfy
    ' both by naming them together after Implements — VB is explicit about
    ' which interface member each implementation maps to, so the ambiguity is
    ' resolved at the declaration rather than at the call.
    Public Class BothAtOnce
        Implements IReader, IWriter

        Public Function Read() As String Implements IReader.Read, IWriter.Read
            Return "shared"
        End Function
    End Class

    ' Alternatively each interface gets its own implementation, under any name,
    ' because the Implements clause carries the mapping rather than the name.
    Public Class SeparateImplementations
        Implements IReader, IWriter

        Public Function ReadForReader() As String Implements IReader.Read
            Return "reader"
        End Function

        Public Function ReadForWriter() As String Implements IWriter.Read
            Return "writer"
        End Function
    End Class

    Public Module Usage
        ' The interface used to reach the object decides which body runs.
        Public Function ThroughReader() As String
            Dim value As IReader = New SeparateImplementations()
            Return value.Read()
        End Function

        Public Function ThroughWriter() As String
            Dim value As IWriter = New SeparateImplementations()
            Return value.Read()
        End Function
    End Module
End Namespace
