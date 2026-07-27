namespace Net10_CSharp13_Library.CSharp14.IgnoredDirectives
{
    // This row cannot be demonstrated inside any project in this corpus, and
    // the file is comments-only for that reason. The runnable demonstration
    // lives outside every project directory, at
    //
    //   examples/language-features/CSharp/dotnet/10/file-based/IgnoredDirectives.cs
    //
    // and is exercised with `dotnet run IgnoredDirectives.cs`, not by building
    // any project here.
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
    // WHAT THIS ROW'S PLACEMENT DOES AND DOES NOT CLAIM. Every other row in the
    // corpus earns its folder by failing to compile one language version below
    // it. This one cannot: the directives are not gated on <LangVersion> at all.
    // A file-based program pinned with `#:property LangVersion=12.0`, `=13.0`
    // and `=14.0` runs identically in all three cases. What gates them is the
    // compilation MODE, -features:FileBasedProgram, which the SDK sets only when
    // it builds a file-based program.
    //
    // So the feature shipped with the .NET 10 SDK alongside C# 14.0, and sits
    // under CSharp14 for that reason alone — its shipping vehicle, not a
    // language gate. No LangVersion pin can confirm or refute that placement,
    // which is also why this row is present in every pinned project rather than
    // only the ones at C# 14.0 and above.
    //
    // Every claim above was verified rather than assumed: these directives
    // placed at the top of this file produced CS9298, the same directives in a
    // standalone file run with `dotnet run` executed normally, and the
    // LangVersion sweep was run against the file-based program itself.
    public static class Explanation
    {
    }
}
