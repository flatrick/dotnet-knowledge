#!/usr/bin/env dotnet
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property DefineConstants=IGNORED_DIRECTIVES_ROW

// Run this file directly, with no project file:
//
//     dotnet run IgnoredDirectives.cs
//
// The `#:` lines above are the feature. They belong to a FILE-BASED PROGRAM: a
// single .cs file the SDK builds without a .csproj. The SDK reads them to
// construct the build, and the compiler then SKIPS them instead of rejecting
// them as unknown preprocessor directives — which is what "ignored directives"
// names, the compiler's half of the contract.
//
// This file deliberately lives outside every project directory. The SDK's
// default glob is **/*.cs, so a copy inside any corpus project would be
// compiled as ordinary project source and fail with CS9298, "'#:' directives
// can be only used in file-based programs ('-features:FileBasedProgram')".
//
// `#:package <id>@<version>` is the third directive in the family. It is left
// out here on purpose: it would make running this file require a NuGet
// download, and the corpus is meant to build and run offline.
//
// SCOPE NOTE — this row is not gated on <LangVersion>. `#:property
// LangVersion=12.0`, `=13.0` and `=14.0` all run this program successfully, so
// no language version rejects the directives. What gates them is the
// compilation MODE (-features:FileBasedProgram), which the SDK sets only for a
// file-based program. The feature shipped with the .NET 10 SDK alongside
// C# 14.0; it is not a C# 14.0 language feature, and no LangVersion pin can
// prove or disprove its placement.

using System;
using System.Runtime.InteropServices;

// Set by the `#:property DefineConstants` directive above. If the SDK had not
// consumed that line and passed it through to the compiler, this would print
// "no" — so the output is evidence the directive took effect, not decoration.
#if IGNORED_DIRECTIVES_ROW
const string PropertyReachedCompiler = "yes";
#else
const string PropertyReachedCompiler = "no";
#endif

Console.WriteLine("ran as a file-based program, no .csproj involved");
Console.WriteLine("#:property reached the compiler: " + PropertyReachedCompiler);
Console.WriteLine("#:property TargetFramework took effect: " + RuntimeInformation.FrameworkDescription);
