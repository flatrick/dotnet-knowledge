# Agent setup — dotnet-knowledge

This file is the entry point for LLM agents and contributors. Read it before doing anything else.
For server work, read [docs/design/mcp-tool-surface.md](docs/design/mcp-tool-surface.md); for known
deferred work, read [docs/backlog/README.md](docs/backlog/README.md).

## What this repository is

Two things that serve one purpose — giving an agent trustworthy, local, version-pinned answers about
C#, VB.NET and Roslyn:

1. **`examples/language-features/`** — an authored corpus of every C# and VB.NET language feature,
   one example per feature per language version across several TFM/project-format combinations,
   plus a curated C# script showcase. It is complete against `MANIFEST.md`, which is the count of
   record; every project **in the build matrix** builds at 0 errors and 0 warnings, and every script
   scenario has verified host behavior. The matrix is 45 of the corpus's 53 projects; what is in it,
   what is deliberately outside it, and what is still an open gap are under "Building the corpus"
   below.
2. **`src/`** — an MCP stdio server that serves that corpus, plus API and language-design docs
   fetched from upstream Microsoft repositories.

**The caller is always an agent.** Humans read the corpus directly in an editor or on GitHub; they
do not go through the MCP. Design every response for context economy and machine parsing, not for
human browsing — structured payloads, no ASCII tables, no decorative formatting.

## Directory map

| Path | Purpose |
|------|---------|
| `examples/language-features/` | The corpus. `MANIFEST.md` is its index and completion oracle. |
| `examples/language-features/MANIFEST.md` | Every language-feature row and script scenario; the corpus index and count of record. Read this before touching the corpus. |
| `examples/language-features/CSharp/csx/roslyn-5.6.0/` | Eight descriptor-backed, BCL-only C# script scenarios plus the pinned embedding host. |
| `docs/design/mcp-tool-surface.md` | The server's tool surface, provenance envelope, and sync model. |
| `docs/design/language-feature-showcase-design.md` | Why the corpus is shaped the way it is: era probes, applicability rule, the derivation model. |
| `docs/design/ci.md` | What CI runs and when, why every version is pinned, and the runbook to activate the merge gate. |
| `docs/domain/csharplang-map.md`, `docs/domain/vblang-map.md` | Quick-find maps for the upstream language-design repos. |
| `docs/backlog/` | Known issues and deferred decisions, one file each. Read before assuming a rough edge is undiscovered. |
| `sources.json` | The upstream sources, their pinned commits, and their sparse-checkout paths. |
| `scripts/*.cs` | Dev tooling, as single-file C# programs — `dotnet scripts/foo.cs -- <args>`. |
| `version.json` | Nerdbank.GitVersioning config. The package version carries the commit it was built from, which is how the install script tells a current tool from a stale one. |

## Prerequisites

**Run `dotnet scripts/install-mcp-tool.cs -- install` once per machine, before the first session
that uses the `dotnet-knowledge` tools.** `.mcp.json` and `.codex/config.toml` start the server with
the `dotnet-knowledge` command from `~/.dotnet/tools`, which this install creates.

The install is machine-global, not per-checkout: a worktree needs no MCP setup of its own, and
installing from one worktree changes the server for every concurrent session. The script self-locates
from its own file path, so a worktree's copy always installs *that worktree's* code regardless of the
shell's working directory.

Re-run the same command after changing server code, then **restart the MCP connection** — a running
client keeps serving the previously installed binary until it reconnects. With no arguments the
script reports the installed version and whether it was built from this checkout's HEAD.

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
   [`docs/decisions.md`](docs/decisions.md) and [`docs/gotchas.md`](docs/gotchas.md) are the two
   deliberate exceptions: they are append-only, dated, and never edited, because a superseded entry
   is what stops a settled question being reopened. Do not tidy them.
4. **American English** for identifiers, comments, and prose, except where an external standard
   specifies otherwise (the Model Context Protocol's `notifications/cancelled` stays as spelled).
5. **Never commit upstream content.** The sources in `sources.json` are fetched into a per-user
   cache at runtime — never vendored, never submoduled, never pasted into a doc. Everything tracked
   here is authored here, which is what makes the MIT grant in `LICENSE` true. Do not point the
   fetch cache at the working tree and then stage the result, and do not re-add the `dotnet-mcp`
   submodules out of habit. [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) states the policy;
   `dotnet scripts/verify-no-vendored-content.cs` enforces it and exits 1 on a finding.

## Working on the corpus

**C# scripts have a Roslyn host coordinate, not an SDK/TFM project coordinate.** The tree at
`CSharp/csx/roslyn-5.6.0/` is outside project discovery. All eight descriptors execute through
`Microsoft.CodeAnalysis.CSharp.Scripting` 5.6.0; the five that name `csi` also execute through the
matching `Microsoft.Net.Compilers.Toolset` 5.6.0 `tasks/net472/csi.exe` on Windows. Scripts and
support files use only the BCL. The net10 embedding host does not turn `.csx` into a .NET 10
file-based `.cs` program, and its restricted resolvers do not make script execution a sandbox:
these are trusted programs running with the host process's permissions. The host requests
cooperative cancellation after 30 seconds and on Ctrl+C, but a script that does not observe
cancellation may require the caller to terminate the host process. Keep the manifest rows, scenario
descriptors, and recursive file inventories in exact agreement.

**The per-`<LangVersion>` trees are hand-authored probes.** They replace the older derived-project
layout, and the tracked tree is current truth. `scripts/generate-net48-examples.cs` still targets
deleted project roots; do not run it against this tree. Scope edits to the projects named by the
task. When a task requires an authored C# sample to remain identical across cumulative project pins,
propagate it explicitly and verify byte equality.

**VB does not duplicate its sources.** Each VB family — `VB.NET/dotnet/Net10/` and
`VB.NET/dotNetFramework/v4.8/` — keeps one copy of every row under `src/`, and a project per pinned
`<LangVersion>` selects rows from it with `Compile` globs. A VB compilation prepends `RootNamespace`
itself, so one file serves every pin. Edit the file under `src/`; there is no second copy to keep in
step, and `VbSourceCoverageTests` fails if a row under `src/` is compiled by no project.
`MyType=Windows` is per-compilation and lives only in the net48 family's `my/` projects.

**Building the corpus.** `CorpusProjectBuildTests` discovers and builds four roots at 0 errors and
0 warnings: the SDK-style C# **library** projects under
`examples/language-features/CSharp/dotnet/`, every SDK-style project under
`examples/language-features/CSharp/dotNetFramework/`, every VB project under
`examples/language-features/VB.NET/`, and — through Visual Studio's `MSBuild.exe` rather than the
SDK host — every legacy non-SDK project under `examples/language-features/CSharp/dotNetFramework/`.
`CorpusProjectDiscoveryTests` holds the exact expected list; that is the count of record.

That is 45 of the corpus's 54 projects. Three of the remaining nine are outside it deliberately:
`CSharp/CSharpComTypeLib/` and `CSharp/CSharpRefReturnLib/` are prebuilt as shared references at the
same bar, and `CSharp/csx/roslyn-5.6.0/host/` has a Roslyn host coordinate rather than an SDK/TFM
one. The other six — three `exe` and three `unsafe` projects under `CSharp/dotnet/` — are built by
nothing and referenced by nothing, which is an open gap rather than a decision:
[`docs/backlog/csharp-dotnet-exe-and-unsafe-projects-are-in-no-build-matrix.md`](docs/backlog/csharp-dotnet-exe-and-unsafe-projects-are-in-no-build-matrix.md).

The eleven legacy projects need Visual Studio's `MSBuild.exe` on Windows — `dotnet build` restores
their `PackageReference` items and then resolves none of them, failing with `CS0246` on `Span` and
`ValueTask` while saying nothing about the toolchain. That host is machine-installed rather than
supplied by the repository-private test host, so **on a machine without Visual Studio those eleven
report inconclusive**, naming the `vswhere` path that was inspected. `CSharp_v8.0`,
`CSharpRefReturnLib` and the eleven net48 VB projects — the last through their family's
`Directory.Build.props` — carry `Microsoft.NETFramework.ReferenceAssemblies` and so need no
machine-installed targeting pack. No other net48 project does: the two SDK-style `*-Unsafe` net48
projects have no props above them supplying it, and a legacy non-SDK project cannot consume the
package at all.

`CSharp_v1.0` and `CSharp_v1.0-Unsafe` both carry `GenerateTargetFrameworkAttribute=false`. Any
project pinned to C# 1.x needs it, SDK-style or not: the generated `AssemblyAttributes.cs` spells
its `TargetFramework` attribute with `global::`, and the resulting `CS8022` names a generated file
rather than a sample, so it reads as a broken corpus.

**Corpus verification has three layers.**

1. Project builds prove validity at a declared SDK, TFM, and language-version coordinate.
2. Isolated compilation cases prove positive and negative feature boundaries.
3. Runtime cases prove comments that assert observable behavior.

Every project build still requires 0 errors and 0 warnings. `TreatWarningsAsErrors` and
`MSBuildTreatWarningsAsErrors` are both inherited so that layer is mechanical; the first reaches the
compilers and NuGet, the second reaches MSBuild's own `MSB####` warnings, and only the pair makes
the exit code sufficient on its own. Never add a `#pragma warning disable` to get past a warning. A
clean build does not prove a sample demonstrates its feature, because cumulative projects can accept
constructs outside the row's intended boundary.

Install or verify the exact test SDKs with `dotnet scripts/install-corpus-test-sdks.cs`; see
[`scripts/install-corpus-test-sdks.md`](scripts/install-corpus-test-sdks.md) for the private-host
test command. An older TFM under SDK 10 uses SDK 10's compiler against the older reference pack; it
does not emulate or select that TFM's historical compiler. Keep SDK, TFM, `LangVersion`, and runtime
execution independent in every case.

Any new comment that asserts observable runtime behavior must include the language-appropriate
marker in the canonical authored source: `// Runtime verification: <case-id>` in C# or
`' Runtime verification: <case-id>` in VB. The ID must name exactly one case with a nonempty
`runtimes` array, and the marker must occur in the canonical source path named by that case.
Compilation cannot check claims such as "this is a view" or "this rounds to even."

**A pinned `<LangVersion>` does not prove it either.** Roslyn holds a construct to a language
version only where its binder calls `CheckFeatureAvailability`. Syntax-driven features all got that
call; semantic and attribute-driven ones did not, so `/langversion:6` on a current compiler is not
the C# 6 compiler. Two C# rows are known to compile far below their own version —
`GeneralizedAsyncReturnTypes` (C# 7.0) and `Variance` (C# 2.0) — and VB's post-14 rows are ungated
far more often than they are gated, because VB's later releases add recognition rather than syntax.

**`MANIFEST.md`'s two version columns are two different quantities and must never be merged.** VB's
**Measured floor** is placement-derived: the lowest pin whose project compiles the row, derivable
there because every VB row is placed at every pin that compiles it. C#'s **Lowest accepted
`/langversion`** is probe-derived: the lowest rung the installed compiler still accepts the row's
source at, found by walking the ladder down. They answer different questions, and on the rows this
script exists to find they disagree — `GeneralizedAsyncReturnTypes` is `UNGATED` at a native ceiling
because a real C# 6 compiler rejects it, while today's compiler takes it down to `/langversion:5`.

```bash
dotnet scripts/verify-feature-floors.cs                          # classify every group folder
dotnet scripts/verify-feature-floors.cs -- --project CSharp_v7.0 # one C# project
dotnet scripts/verify-feature-floors.cs -- --language vb         # the VB ladder, both families
dotnet scripts/verify-feature-floors.cs -- --language vb --project dotnet/Net10/15.5/library
dotnet scripts/verify-feature-floors.cs -- --json                # machine-readable
```

`--project` takes a directory name for C# (`CSharp_v7.0`) and a `<family>/<pin>/<kind>` path
relative to `examples/language-features/VB.NET` for VB (`dotnet/Net10/15.5/library`,
`dotNetFramework/v4.8/latest/my`).

It compiles each group folder standalone at its own version, again one rung down, and escalates to a
compiler that natively tops out at that lower rung: Microsoft.Net.Compilers 1.3.2 for C# 6 / VB 14
(cached under `.artifacts/`), and the in-box `%WINDIR%\Microsoft.NET\Framework64` compilers for
C# 5 / VB 11 (`v4.0.30319`), C# 3 / VB 9 (`v3.5`) and C# 2 (`v2.0.50727`). Those old compilers read
net48 reference assemblies without complaint, so no era-specific projects are needed to drive them.

**C# 4 and C# 1.x have no compiler.** .NET 4.5 upgraded `v4.0.30319`'s csc in place from C# 4 to
C# 5, so the C# 4 binary is gone from any machine with .NET 4.5 or later, and .NET 1.0/1.1 do not
install on a current Windows. Floors at those versions report `UNPROVEN` rather than a guess. VB's
gaps are VB 10 and VB 12, and nothing above VB 14 has a native ceiling at all.

**Every verdict carries an `evidence` field**, because a pinned modern compiler and a period
compiler are not the same claim: `native-ceiling` (a compiler topping out at the rung below settled
it — stable), `legacy-pin` (a pre-Roslyn compiler held to that rung; a rejection proves version
dependence, an acceptance proves nothing), `sdk-pin` (only the installed SDK under `/langversion` —
a fact about today's toolchain, which drifts), `exempt` (no probe evidence is possible for this row
at all, which is a different claim from having gathered none), and `none`.

`MISPLACED`, `NOT-VERSION-SPECIFIC` and `UNDER-PLACED` fail the run. `UNGATED`, `UNPROVEN`,
`BASELINE` and `INCONCLUSIVE` report what the available compilers can and cannot settle. `EXEMPT`
covers rows a floor probe structurally cannot judge — `LockStatement` (filed under C# 3.0 to mirror
the source document, though `lock` is C# 1.0), `EmbeddedInteropTypes` (NoPIA lives in the reference,
not the source), the three VB 17.13 consumption rows (`UnmanagedConstraintRecognition`,
`CallerArgumentExpressionConsumption`, `OverloadResolutionPriorityConsumption` — each demonstrates
the compiler honoring metadata a C# assembly emitted, which no `LangVersion` gates), and VB's
`Baseline` bucket (it spans VS.NET 2002 to VS2012, so no single previous-version pin is meaningful;
it gets the own-version check only). Windows and Visual Studio's MSBuild only.

**`UNDER-PLACED` is the converse of `MISPLACED`.** A project holds *every* row that compiles at its
pin, so a row a project could build and does not claim is as much a defect as one it claims and
cannot. The check compiles each unclaimed `src/` row at the pin; VB only, since C# projects own
their rows on disk rather than globbing a shared tree.

**`MISPLACED` means a stale project file.** A project holds every row that compiles at its pin,
including rows filed above it that `LangVersion` does not gate, so being filed above the pin is the
corpus's model rather than an error. Such a row is compiled at the pin first: accepted, it goes
through the normal floor probe with a note recording the placement; rejected at the pin but accepted
at its own version, it is `MISPLACED`, because the project claims a row it cannot build; rejected at
both, it is `INCONCLUSIVE`, because the harness rather than the pin is what failed.

## Continuous integration

**No CI runs. Actions is disabled for this repository, and verification is whatever you run
locally.** Workflow configuration is still maintained — `.github/workflows/corpus-tests.yml` is
current and a new job should be added when new tests are — but nothing executes it. Minutes cost
money on a private repository, and while this is a personal project that cost buys nothing a local
run does not already provide.

Two consequences follow, and both are easy to get wrong:

- **A commit's presence on `main` says nothing about whether it was verified.** Not "the gate is
  advisory" — there is no run to read. If you need to know whether something passes, run it.
- **Adding a test does not make it run anywhere.** A suite nobody invokes locally is dead weight
  dressed as coverage, so a new suite needs an entry in [`CLAUDE.md`](CLAUDE.md)'s command list, not
  only a workflow step.

[`docs/design/ci.md`](docs/design/ci.md) has the workflow's design and the runbook for turning
Actions on and activating a merge gate, for whenever that becomes worth paying for.
