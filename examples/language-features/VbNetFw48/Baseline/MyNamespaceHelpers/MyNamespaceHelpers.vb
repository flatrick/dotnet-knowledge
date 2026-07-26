Option Strict On

Namespace Baseline.MyNamespaceHelpers
    ' The `My` namespace is VB's own compiler-generated accessor layer over a set
    ' of framework services — it is not a library you import but a namespace the
    ' VB compiler synthesizes into the assembly, driven by the MyType property in
    ' the project file. That is why this row lives only here: this project sets
    ' MyType=Windows, whereas the SDK passes _MyType=Empty for a net10.0 VB class
    ' library, leaving no `My` member to demonstrate at all.
    '
    ' Nothing below calls into anything a class library could not reach. The
    ' point is the accessor layer itself, not the services behind it.
    Public Module MyHelpers
        ' My.Computer.Info wraps environment queries that would otherwise mean
        ' reaching for System.Environment or WMI directly.
        Public Function OperatingSystemName() As String
            Return My.Computer.Info.OSFullName
        End Function

        Public Function TotalPhysicalMemory() As ULong
            Return My.Computer.Info.TotalPhysicalMemory
        End Function

        ' My.Computer.Name is the machine name — the same value as
        ' Environment.MachineName, reached through the VB-specific surface.
        Public Function MachineName() As String
            Return My.Computer.Name
        End Function

        ' My.Computer.Clock separates local from UTC without the caller having
        ' to remember which DateTime property means which.
        Public Function LocalNow() As Date
            Return My.Computer.Clock.LocalTime
        End Function

        Public Function UtcNow() As Date
            Return My.Computer.Clock.GmtTime
        End Function

        ' My.Computer.FileSystem is the largest of the helpers: path composition,
        ' existence checks, and whole-file reads and writes in one call each.
        Public Function CombinePath(folder As String, file As String) As String
            Return My.Computer.FileSystem.CombinePath(folder, file)
        End Function

        Public Function DirectoryExists(path As String) As Boolean
            Return My.Computer.FileSystem.DirectoryExists(path)
        End Function

        ' My.Application.Info reports the identity of the ENTRY assembly, not of
        ' the assembly the call is written in. Called from this class library it
        ' describes whatever executable loaded it — verified by running it from a
        ' console host, which reported the host's name rather than this library's.
        ' That is the useful thing to know about it: the name reads as though it
        ' were local, and it is not.
        Public Function EntryAssemblyName() As String
            Return My.Application.Info.AssemblyName
        End Function

        Public Function EntryAssemblyVersion() As String
            Return My.Application.Info.Version.ToString()
        End Function
    End Module
End Namespace
