# Shared VB `Compile` glob resolution

## Purpose

Reduce the two implementations of VB `Compile` item resolution to one, compiled into both consumers.

Two places resolve a VB project's `Compile` items to a set of files on disk:
`VbSourceCoverageTests` decides whether a corpus row is compiled by anything, and
`verify-feature-floors.cs` decides which rows a project owns. Both read `Include` and `Remove`,
require the `<directory>/**/*.<ext>` glob shape, throw on any other shape, and subtract removals from
inclusions. They agree today. If they drift, the corpus gains a blind spot precisely where two
guards appear to agree — the coverage test would call a row compiled while the floor probe called it
unclaimed, or the reverse, and each would report a clean run.

## The premise that deferred this

`docs/backlog/glob-resolution-is-implemented-twice.md` deferred the item on the claim that "a test
project and a standalone single-file script cannot share code, and this repository's convention is
that tooling is single-file C# with no shared library."

The first half is false on SDK 10. File-based programs support `#:include`, which adds a source file
to the compilation, with relative paths resolved against the containing file. Measured on SDK
10.0.302 with a two-file pair:

| Case | Result |
|---|---|
| `#:include Shared.cs` present | exit 0; the shared namespace binds |
| `#:include` absent, sibling `.cs` in the same directory | `CS0246`, the type does not bind |

The second result is as load-bearing as the first: a file-based program does not glob its directory,
so a shared file placed under `scripts/` reaches exactly the scripts that name it and no others.

## Mechanism

One shared source file, `scripts/shared/CompileItems.cs`:

- `scripts/verify-feature-floors.cs` names it with `#:include shared/CompileItems.cs`.
- `tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj` names it with a linked
  `<Compile Include>`.

Rejected: **a shared library project referenced with `#:project`.** It buys an assembly boundary this
does not need, and costs a new `.csproj`, an entry in `Corpus.slnx`, and a project build on every
run of a script whose whole point is that it needs no project.

Rejected: **keeping both implementations and adding a test that runs the script's `--json` and
compares.** It detects drift rather than preventing it, and it makes a unit test shell out to a probe
that takes minutes.

Rejected: **the status quo.** The backlog file's own suggestion — reconsider when a third consumer
appears — rests on the premise above, which does not hold.

## The shared file

Namespace `DotNetKnowledge.CorpusTooling`, `internal static class CompileItems`, two members:

```csharp
internal static (HashSet<string> Included, HashSet<string> Removed) Resolve(
    string projectPath, string sourceExtension)

internal static IEnumerable<string> ResolveGlob(
    string projectPath, string projectDirectory, string glob, string sourceExtension)
```

`Resolve` walks every `Compile` descendant of the project, unions `Include` globs into the included
set and `Remove` globs into the removed set, subtracts the second from the first, and returns both.
It returns both halves because the consumers need different ones: the coverage test asks only whether
a file survives somewhere, while the floor probe's under-placement check needs the removals — a row a
project deliberately excludes is a policy statement, not a row it forgot.

`ResolveGlob` requires the glob to end in `**/*<extension>` and throws naming the project and the
expected tail otherwise. It does not implement a general glob matcher: this corpus has exactly one
glob shape, and a shape change should fail loudly rather than silently under- or over-count.

### It is compiled by two hosts with different settings

The test project sets `Nullable=enable` and inherits `AnalysisLevel=latest-recommended` and
`TreatWarningsAsErrors=true` from the root `Directory.Build.props`. `scripts/Directory.Build.props`
resets all three for scripts. The file must satisfy the stricter host, and must not change meaning
under the looser one:

- **`#nullable enable` as a file-level directive.** It wins in both hosts, so the file's nullable
  semantics do not depend on which one compiled it. Without it, a `string?` annotation warns `CS8632`
  under the script host.
- **Its own `using` directives.** Usings in `verify-feature-floors.cs` do not reach an `#:include`d
  file.
- **A namespace, not the global one.** `CA1050` would otherwise fail the test build.
- **`internal static` and a plain named tuple**, rather than a public type or a record, to keep the
  public-API analyzer surface at zero.

## Consumers after the change

`VbSourceCoverageTests.CompiledFiles` keeps its `.vbproj` enumeration and its `bin`/`obj` skip; its
per-project body becomes a call to `Resolve` and a union of the included set.

`VbRows` in `verify-feature-floors.cs` keeps `RowKey`, the bucketing into
`src/<version folder>/<group>/`, the `RowGroup` ordering and the `VbProjectRows` return; it opens with
a call to `Resolve` and uses both halves. `UnclaimedVbRows` and `LanguageProfile` are untouched.

## Verification

The acceptance oracle is that the VB floor probe's `--json` output is byte-identical across the
change: `dotnet scripts/verify-feature-floors.cs -- --language vb --json`, captured before and
after, diffed. That payload carries every VB verdict, its evidence class, and the rows each project
claims, so a resolution difference of any size moves it.

Alongside it: `verify-project-namespaces.cs` and `verify-no-vendored-content.cs` still exit 0, which
also confirms no other script picked up the new file; `Corpus.slnx` builds; `VbSourceCoverageTests`
passes through the repository-private host; and a deliberately malformed `Compile Include` still
makes both consumers throw with the project named, since a shared implementation that quietly
returned an empty set would defeat both guards at once.

## Consequence for the conventions

`AGENTS.md` convention 1 reads "Tooling is single-file C#." Its actual constraint is that a tool runs
on native Windows with only the .NET SDK and is never a shell script; `#:include` preserves both, and
the convention is restated to say so. `scripts/shared/` is for code a test must also compile, and
nothing else — a script that merely wants to reuse a helper from another script has not met that bar.
