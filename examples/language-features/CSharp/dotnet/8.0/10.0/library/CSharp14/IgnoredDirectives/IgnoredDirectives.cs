namespace CSharpNet7_10.CSharp14.IgnoredDirectives;

// This row cannot be demonstrated inside any project in this corpus, and
// the file is comments-only for that reason.
//
// A `#:` directive configures a FILE-BASED PROGRAM — a single .cs file run
// directly with `dotnet run app.cs`, with no project file. The lines tell
// the SDK what to build, before any compiler sees the file:
//
//   #!/usr/bin/env dotnet
//   #:sdk Microsoft.NET.Sdk
//   #:package Newtonsoft.Json@13.0.3
//   #:property LangVersion=preview
//
//   System.Console.WriteLine("hello");
//
// The name "ignored directives" describes the COMPILER's half of the
// contract: the SDK consumes these lines, and the compiler then skips them
// instead of rejecting them as unknown preprocessor directives.
//
// That contract holds only in a file-based program. Placed in a file
// belonging to a project, the same directives are a hard error —
// CS9298, "'#:' directives can be only used in file-based programs
// ('-features:FileBasedProgram')" — so writing them here would not
// demonstrate the feature, it would break the build.
//
// Both halves were verified rather than assumed: these directives placed at
// the top of this file produced CS9298 three times, and the same directive
// in a standalone file run with `dotnet run` executed normally.
public static class Explanation
{
}
