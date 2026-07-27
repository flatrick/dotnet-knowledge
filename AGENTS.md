# Agent setup — dotnet-knowledge

This file is the entry point for LLM agents and contributors. Read it before doing anything else,
then read [docs/HANDOFF.md](docs/HANDOFF.md) for the current state of the build and what to do next.

## What this repository is

Two things that serve one purpose — giving an agent trustworthy, local, version-pinned answers about
C#, VB.NET and Roslyn:

1. **`examples/language-features/`** — an authored corpus of every C# and VB.NET language feature,
   one example per feature per language version, across several TFM/project-format combinations. It
   is complete against `MANIFEST.md`, which is the count of record; every project builds at 0 errors
   and 0 warnings.
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
| `docs/design/ci.md` | What CI runs and when, why every version is pinned, and the runbook to activate the merge gate. |
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
5. **Never commit upstream content.** The sources in `sources.json` are fetched into a per-user
   cache at runtime — never vendored, never submoduled, never pasted into a doc. Everything tracked
   here is authored here, which is what makes the MIT grant in `LICENSE` true. Do not point the
   fetch cache at the working tree and then stage the result, and do not re-add the `dotnet-mcp`
   submodules out of habit. [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) states the policy;
   `dotnet scripts/verify-no-vendored-content.cs` enforces it and exits 1 on a finding.

## Working on the corpus

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

**Building the corpus.** `CorpusProjectBuildTests` discovers and builds the SDK-style C# library
projects under `examples/language-features/CSharp/dotnet/` and every VB project under
`examples/language-features/VB.NET/`, all at 0 errors and 0 warnings.
`CorpusProjectDiscoveryTests` holds the exact expected list; that is the count of record. The legacy
`examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v7.0/CSharp70.csproj` needs Visual
Studio's `MSBuild.exe` on Windows — `dotnet build` restores its `PackageReference` items and then
resolves none of them, failing with `CS0246` on `Span` and `ValueTask` while saying nothing about
the toolchain. The net48 SDK-style projects — the whole VB net48 family and `CSharp_v8.0` — carry
`Microsoft.NETFramework.ReferenceAssemblies`, so they need no machine-installed targeting pack.

**Corpus verification has three layers.**

1. Project builds prove validity at a declared SDK, TFM, and language-version coordinate.
2. Isolated compilation cases prove positive and negative feature boundaries.
3. Runtime cases prove comments that assert observable behavior.

Every project build still requires 0 errors and 0 warnings. `TreatWarningsAsErrors` is inherited so
that layer is mechanical; never add a `#pragma warning disable` to get past a warning. A clean build
does not prove a sample demonstrates its feature, because cumulative projects can accept constructs
outside the row's intended boundary.

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
`MANIFEST.md`'s **Measured floor** column records what each VB row actually needs.

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
a fact about today's toolchain, which drifts), and `none`.

`MISPLACED` and `NOT-VERSION-SPECIFIC` fail the run. `UNGATED`, `UNPROVEN`, `BASELINE` and
`INCONCLUSIVE` report what the available compilers can and cannot settle. `EXEMPT` covers rows a
floor probe structurally cannot judge — `LockStatement` (filed under C# 3.0 to mirror the source
document, though `lock` is C# 1.0), `EmbeddedInteropTypes` (NoPIA lives in the reference, not the
source), and VB's `Baseline` bucket (it spans VS.NET 2002 to VS2012, so no single previous-version
pin is meaningful; it gets the own-version check only). Windows and Visual Studio's MSBuild only.

**`MISPLACED` means a stale project file.** A project holds every row that compiles at its pin,
including rows filed above it that `LangVersion` does not gate, so being filed above the pin is the
corpus's model rather than an error. Such a row is compiled at the pin first: accepted, it goes
through the normal floor probe with a note recording the placement; rejected at the pin but accepted
at its own version, it is `MISPLACED`, because the project claims a row it cannot build; rejected at
both, it is `INCONCLUSIVE`, because the harness rather than the pin is what failed.

## Continuous integration

**A green CI run does not mean a commit was gated.** `.github/workflows/corpus-tests.yml` runs on
every pull request to `main`, but nothing blocks a merge on its result and nothing blocks a direct
push to `main` — required status checks need a ruleset, which GitHub withholds while this
repository is private on the Free plan. Do not infer from a commit's presence on `main` that
verification passed for it; read the run. [`docs/design/ci.md`](docs/design/ci.md) has the
reasoning and the runbook to activate the gate once the repository is public.
