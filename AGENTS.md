# Agent setup — dotnet-knowledge

This file is the entry point for LLM agents and contributors. Read it before doing anything else,
then read [docs/HANDOFF.md](docs/HANDOFF.md) for the current state of the build and what to do next.

## What this repository is

Two things that serve one purpose — giving an agent trustworthy, local, version-pinned answers about
C#, VB.NET and Roslyn:

1. **`examples/language-features/`** — an authored corpus of every C# and VB.NET language feature,
   one example per feature per language version, across four TFM/project-format combinations. It is
   complete: 169 C# rows and 58 VB rows, every project building at 0 errors and 0 warnings.
2. **`src/`** — an MCP stdio server that serves that corpus, plus API and language-design docs
   fetched from upstream Microsoft repositories.

**The caller is always an agent.** Humans read the corpus directly in an editor or on GitHub; they
do not go through the MCP. Design every response for context economy and machine parsing, not for
human browsing — structured payloads, no ASCII tables, no decorative formatting.

## Directory map

| Path | Purpose |
|------|---------|
| `examples/language-features/` | The corpus. `MANIFEST.md` is its index and completion oracle. |
| `examples/language-features/MANIFEST.md` | Every feature row, its group folder, target projects, and any exclusion reason. Read this before touching the corpus. |
| `docs/HANDOFF.md` | **Start here.** Current build state, next steps, and open decisions. |
| `docs/design/mcp-tool-surface.md` | The server's tool surface, provenance envelope, and sync model. |
| `docs/design/language-feature-showcase-design.md` | Why the corpus is shaped the way it is: era probes, applicability rule, the derivation model. |
| `docs/domain/csharplang-map.md`, `docs/domain/vblang-map.md` | Quick-find maps for the upstream language-design repos. |
| `sources.json` | The upstream sources, their pinned commits, and their sparse-checkout paths. |
| `scripts/*.cs` | Dev tooling, as single-file C# programs — `dotnet scripts/foo.cs -- <args>`. |

## Conventions inherited deliberately

These come from `flatrick/dotnet-mcp`, where this content originated. They are kept because they
earned their place, not out of habit.

1. **Tooling is single-file C#, never a shell script.** No `.sh`, `.ps1`, `.bat`, or `.py`. Every
   tool must run on native Windows with only the .NET SDK. Pass arguments after `--`, because
   `dotnet <file>.cs` silently claims some flags for itself.
2. **No silent truncation.** A capped result set must say so — `isPartial`, `nextPageToken`, or
   similar. An agent that receives a quietly-truncated search concludes the symbol does not exist.
3. **State current truth only.** Documents do not narrate their own history; `git log` is the
   changelog. No "previously said X" footers, no dated verification stamps.
4. **American English** for identifiers, comments, and prose, except where an external standard
   specifies otherwise (the Model Context Protocol's `notifications/cancelled` stays as spelled).

## Working on the corpus

**The net48 projects are derived, not hand-written.** `CSharpFw48Cs73`, `CSharpFw48Cs80` and
`VbNetFw48` are generated from `CSharpNet10Latest` and `VbNetNet10Latest` by
`dotnet scripts/generate-net48-examples.cs`. Fix a shared sample in the net10 project and
regenerate; never edit a generated file.

```bash
dotnet scripts/generate-net48-examples.cs             # regenerate
dotnet scripts/generate-net48-examples.cs -- --check  # fail on drift
```

Two `VbNetFw48` rows are hand-authored and exempt from the drift check — `MyNamespaceHelpers` (no
net10 counterpart) and `ConsumingCSharpRefReturnValues` (its net48 subject differs). Anything else
diverging from its source is drift, not a variant.

**Building the corpus.** Six of the seven projects build with `dotnet build`. `CSharpFw48Cs73` is
legacy non-SDK XML and needs Visual Studio's `MSBuild.exe` on Windows — `dotnet build` restores its
`PackageReference` items and then resolves none of them, failing with `CS0246` on `Span` and
`ValueTask` while saying nothing about the toolchain.

**The completion gate is 0 errors and 0 warnings**, and it is the *only* gate — no test in this
repository observes the corpus. `TreatWarningsAsErrors` is inherited so the gate is mechanical.
Never add a `#pragma warning disable` to get past a warning.

**A clean build does not prove a sample demonstrates its feature.** Every project compiles at
`LangVersion=latest`, so a sample that reaches forward to a newer construct still builds. Run both
era probes described in `docs/design/language-feature-showcase-design.md`, and verify by
**execution** any comment that claims runtime behavior — compilation cannot check "this is a view"
or "this rounds to even". That check has already caught claims that passed every other gate.

**A pinned `<LangVersion>` does not prove it either.** Roslyn holds a construct to a language
version only where its binder calls `CheckFeatureAvailability`. Syntax-driven features all got that
call; semantic and attribute-driven ones did not, so `/langversion:6` on a current compiler is not
the C# 6 compiler. Two corpus rows are known to compile far below their own version —
`GeneralizedAsyncReturnTypes` (C# 7.0) and `Variance` (C# 2.0).

```bash
dotnet scripts/verify-feature-floors.cs                          # classify every group folder
dotnet scripts/verify-feature-floors.cs -- --project CSharp_v7.0 # one project
dotnet scripts/verify-feature-floors.cs -- --json                # machine-readable
```

It compiles each group folder standalone at its own version, again one rung down, and escalates to a
compiler that natively tops out at that lower rung: Microsoft.Net.Compilers 1.3.2 for C# 6 (cached
under `.artifacts/`), and the in-box `%WINDIR%\Microsoft.NET\Framework64` compilers for C# 5
(`v4.0.30319`), C# 3 (`v3.5`) and C# 2 (`v2.0.50727`). Those old compilers read net48 reference
assemblies without complaint, so no era-specific projects are needed to drive them.

**C# 4 and C# 1.x have no compiler.** .NET 4.5 upgraded `v4.0.30319`'s csc in place from C# 4 to
C# 5, so the C# 4 binary is gone from any machine with .NET 4.5 or later, and .NET 1.0/1.1 do not
install on a current Windows. Floors at those versions report `UNPROVEN` rather than a guess.

`MISPLACED` and `NOT-VERSION-SPECIFIC` fail the run. `UNGATED`, `UNPROVEN`, `BASELINE` and
`INCONCLUSIVE` report what the available compilers can and cannot settle. `EXEMPT` covers rows a
floor probe structurally cannot judge — `LockStatement` (filed under C# 3.0 to mirror the source
document, though `lock` is C# 1.0) and `EmbeddedInteropTypes` (NoPIA lives in the reference, not the
source). Windows and Visual Studio's MSBuild only.
