# The `exe` and `unsafe` projects under `CSharp/dotnet/` are in no build matrix

`CorpusProjectDiscovery`'s `CSharp/dotnet/` root selects SDK-style projects that are libraries and
do not set `AllowUnsafeBlocks`. Six projects under that root are neither:

```
examples/language-features/CSharp/dotnet/10/9.0/exe/exe.csproj
examples/language-features/CSharp/dotnet/10/14.0/exe/exe.csproj
examples/language-features/CSharp/dotnet/10/latest/exe/exe.csproj
examples/language-features/CSharp/dotnet/10/13.0/unsafe/unsafe.csproj
examples/language-features/CSharp/dotnet/10/14.0/unsafe/unsafe.csproj
examples/language-features/CSharp/dotnet/10/latest/unsafe/unsafe.csproj
```

No test builds them, and nothing else reaches them either: no project in the corpus carries a
`ProjectReference` to any of the six, so they are not built as a dependency the way
`CSharpComTypeLib` and `CSharpRefReturnLib` are.

## Why it matters

These exist for the reason the design doc gives — `AllowUnsafeBlocks` and `OutputType` are
per-compilation switches that cannot be scoped to a folder, so the rows needing them are housed in
their own projects rather than allowed to change the mainline projects' compilation. That makes them
ordinary corpus projects holding ordinary corpus rows, and the corpus's central claim is that every
such project is held to 0 errors and 0 warnings mechanically rather than by discipline.

`CorpusProjectDiscoveryTests` asserts their absence explicitly:

```csharp
Assert.IsFalse(actual.Any(project => project.RepositoryRelativePath.Contains("/unsafe/", ...)));
Assert.IsFalse(actual.Any(project => project.RepositoryRelativePath.Contains("/exe/", ...)));
```

So the exclusion is deliberate and checked, but no recorded decision says why building them would be
wrong — and the parallel case has since been settled the other way. The `CSharp/dotNetFramework/`
root takes every SDK-style project it finds, `AllowUnsafeBlocks` and `OutputType` included, because
excluding them would leave a project the corpus authored and no test builds.

## Suggested fix

Select `CorpusProjectKind.SdkStyle` for the `CSharp/dotnet/` root as well, add the six to
`CorpusProjectDiscoveryTests`'s expected list, and replace the two `Assert.IsFalse` exclusions with
the entries. An `exe` project builds under `dotnet build -t:Rebuild --no-dependencies` exactly as a
library does; nothing about the toolchain keeps these out.

Before doing that, establish whether the exclusion was reasoned. If it was, the outcome is a
`docs/decisions.md` entry rather than six new matrix rows — but the current state records neither.
