# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Orientation

Read [`AGENTS.md`](AGENTS.md) first, then [`docs/HANDOFF.md`](docs/HANDOFF.md) for current build
state and next steps. This file covers commands and the cross-file architecture; AGENTS.md carries
the corpus rules and the reasoning behind them.

The repository is two halves serving one purpose — version-pinned, local answers about C#, VB.NET
and Roslyn:

- **`examples/language-features/`** — a hand-authored corpus of every C# and VB.NET language
  feature, one example per feature per language version, across several TFM/project-format
  combinations.
- **`src/DotNetKnowledge.Mcp/`** — an MCP stdio server that serves that corpus plus API and
  language-design docs fetched from the upstream Microsoft repos pinned in `sources.json`.

**The MCP server's caller is always an agent.** Design responses for context economy and machine
parsing — structured payloads, no ASCII tables, no decorative formatting.

## Commands

```bash
# MCP server
dotnet build src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj
dotnet run --project src/DotNetKnowledge.Mcp        # stdio; expects a client on stdin

# Dev tooling — single-file C# programs; arguments go after `--`
dotnet scripts/verify-no-vendored-content.cs                     # licensing guard; exit 1 on a finding
dotnet scripts/verify-no-vendored-content.cs -- --history        # same checks against every commit
dotnet scripts/verify-feature-floors.cs                          # classify every CSharp_v* group folder
dotnet scripts/verify-feature-floors.cs -- --project CSharp_v7.0 # one project
dotnet scripts/verify-feature-floors.cs -- --json                # machine-readable
dotnet scripts/verify-feature-floors.cs -- --offline             # skip the NuGet download
```

`dotnet <file>.cs` silently claims some flags for itself, which is why every script takes its
arguments after `--`.

The corpus test suite is `tests/DotNetKnowledge.Corpus.Tests/`. Its exact SDK bands live in a
repository-private host. Install or verify them with
`dotnet scripts/install-corpus-test-sdks.cs`, then use the private-host command documented in
[`scripts/install-corpus-test-sdks.md`](scripts/install-corpus-test-sdks.md); do not repeat the SDK
setup manually.

Smoke-testing the server over stdio needs a redirected-process driver, not a shell pipe — a Git Bash
`>` redirect swallows the server's stdout entirely and looks like a server fault.

### Building corpus projects

SDK-style projects (`CSharp/dotnet/10/latest/*`, the VB projects) build with `dotnet build`. The
net48 C# projects are **legacy non-SDK XML and need Visual Studio's `MSBuild.exe` on Windows**:

```bash
# vswhere lives at "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe";
# scripts/verify-feature-floors.cs locates MSBuild the same way.
vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe"
MSBuild.exe examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v7.0/CSharp70.csproj -t:Restore;Build
```

`dotnet build` on those restores their `PackageReference` items and then resolves none of them —
a non-SDK project consumes package assets through NuGet targets that ship with VS, not with the
SDK. It fails with `CS0246` on `Span` and `ValueTask` and says nothing about the toolchain, so it
reads as a broken sample.

## The corpus

Layout is `<language>/<runtime>/<version>/<project>/<version-folder>/<feature-group>/`:

```
examples/language-features/
  CSharp/dotnet/10/latest/{exe,library,unsafe}/        # SDK-style net10.0
  CSharp/dotNetFramework/v4.8/CSharp_v1.0 … CSharp_v8.0   # non-SDK net48, one pinned <LangVersion> each
  CSharp/CSharpComTypeLib/                            # support assembly for the NoPIA row, not a corpus project
  VB.NET/dotnet/Net10/, VB.NET/dotNetFramework/v4.8/   # Baseline/ + Vb15/ … Vb17_13/
  MANIFEST.md                                         # the index and completion oracle
```

Separate `*-Unsafe` and `exe` projects exist because `AllowUnsafeBlocks` and `OutputType` are
per-compilation switches that cannot be scoped to a folder; the mainline projects stay on default
compilation and those rows are housed apart. `docs/design/language-feature-showcase-design.md` has
the applicability rule and the rest of the reasoning.

### Rules that are load-bearing

- **Verification has three layers:** project builds prove validity at a declared SDK/TFM/language
  coordinate; isolated compilation cases prove positive and negative feature boundaries; runtime
  cases prove comments that assert observable behavior.
- **Every project build requires 0 errors AND 0 warnings.** `TreatWarningsAsErrors` is inherited
  from the root `Directory.Build.props` specifically so this layer is mechanical. Never add a
  `#pragma warning disable` to get past a warning, and do not override the property in the corpus
  subtree.
- **An older TFM does not select its historical compiler.** SDK 10 targeting an older TFM uses SDK
  10's compiler against the older reference pack. Keep SDK, TFM, `LangVersion`, and runtime
  execution as separate case inputs.
- **Every new runtime-behavior claim needs a source marker.** Add
  `// Runtime verification: <case-id>` to the canonical authored source, and give that exact case a
  nonempty `runtimes` array. Compilation cannot check "this is a view" or "this rounds to even".
- **A pinned `<LangVersion>` does not prove it either.** Roslyn enforces a version only where the
  binder calls `CheckFeatureAvailability`; syntax-driven features got that call, semantic and
  attribute-driven ones did not. `GeneralizedAsyncReturnTypes` (C# 7.0) compiles at
  `/langversion:5`, `Variance` (C# 2.0) at `ISO-1`. `scripts/verify-feature-floors.cs` settles these
  by escalating to compilers at a *native* ceiling — the in-box `%WINDIR%\Microsoft.NET\Framework64`
  csc for C# 2/3/5, `Microsoft.Net.Compilers` 1.3.2 for C# 6. C# 4 and C# 1.x have no compiler on a
  modern machine, so those floors report `UNPROVEN` rather than a guess. `MISPLACED` and
  `NOT-VERSION-SPECIFIC` fail the run; every other outcome is a finding about the toolchain.
- **Pinning an SDK-style project to `ISO-1`/`ISO-2` needs `GenerateTargetFrameworkAttribute=false`**
  — the SDK's generated `AssemblyAttributes.cs` uses `global::`, so every C# 1.x era probe otherwise
  reports a phantom `CS8022`.
- **Both net48 C# project families need an explicit `Microsoft.CSharp` reference** for the C# 4.0
  `dynamic` row. That failure is `CS0656` at *emit*, so any earlier binding error in the project
  hides it entirely.
- **Probe constructs in isolation.** A whole-project VB build reported 2 errors where per-folder
  builds reported 5; neither compiler announces that it stopped early.

### Current per-version project model

The per-`<LangVersion>` `CSharp_v*` projects are **hand-authored probes**, replacing the older
derived-projects model. Consequences:

- `scripts/generate-net48-examples.cs` still targets the deleted `CSharpNet10Latest` /
  `CSharpFw48Cs73` / `CSharpFw48Cs80` layout. **Do not run it against the new tree.** The
  `GENERATED-COMPILE-ITEMS` markers inside the `CSharp_v*` csproj files are inherited artifacts that
  nothing regenerates.
- Treat the on-disk tree as truth where older planning material or `MANIFEST.md` still uses the
  superseded project names.
- Do not fan an edit across every copy of a sample by default — ask which projects are in scope.

## The MCP server

`Program.cs` is a generic-host stdio server: `AddMcpServer().WithStdioServerTransport()
.WithToolsFromAssembly()`, with `SourceCatalog` (reads `sources.json`) and `SourceCache` (resolves
the on-disk cache) as singletons. Tools live in `Features/<Area>/<Name>Tool.cs` and are discovered by
assembly scan.

**stdout is the protocol channel.** Every log goes to stderr; anything else written to stdout
corrupts the session and surfaces as an opaque client-side parse error. Logging is configured for
this in `Program.cs` — do not add a console provider that writes to stdout.

Cache location defaults to `%LOCALAPPDATA%\dotnet-knowledge\sources` (XDG equivalent elsewhere),
overridable with `DOTNET_KNOWLEDGE_CACHE`. It sits outside any repository so one download serves
every clone and worktree on the machine.

Current state: `list_sources` works. `sync_source`, the API-doc lookups, and the example queries are
not built yet — build order and porting notes are in `docs/HANDOFF.md`, the tool surface in
`docs/design/mcp-tool-surface.md`.

### Non-negotiables for every tool

These are correctness obligations, not preferences:

- **Every payload carries the provenance envelope** — `repo`, `ref` (`pinned` / `head:<branch>` /
  `bundled`), `commit`, `fetchedAt`. Never absent, never inferred. The reason to prefer this server
  over a web search is that its answers are tied to a known revision.
- **No query tool ever triggers a download.** It checks, and fails fast with an imperative remedy
  naming the tool to call. A partially-synced source answers with plausible absences that look like
  real "not found" results — the dangerous failure, because nothing about it looks like an error.
- **No silent truncation.** Every capped result set carries `isPartial` or a cursor. An agent that
  receives a quietly-truncated search concludes the symbol does not exist.
- **`list_sources` keeps returning `cacheDir`.** Structured lookup will not cover everything — the
  corpus itself was built by grepping raw proposal trees — and an agent has no other way to find
  them.
- **Search tools return names and locations, never bodies.** `search_api` returns fully-qualified
  names; `search_language_docs` returns `path:line` hits. The agent then spends context on a single
  `lookup_api` or `get_language_doc`.

## Licensing — never commit upstream content

The repository is MIT (`LICENSE`) and **every tracked file is authored here**. The sources in
`sources.json` are fetched at runtime into a per-user cache outside the working tree; they are never
vendored, submoduled, or pasted into a doc. That is what keeps the MIT grant truthful, and it is the
one invariant here with consequences outside the repository.

The realistic ways it breaks are mundane — pointing the fetch cache at the working tree and
committing it, re-adding the `dotnet-mcp` submodules out of habit, pasting spec text into a doc for
convenience. Run `dotnet scripts/verify-no-vendored-content.cs` before committing anything that adds
files in bulk; it checks tracked paths, third-party copyright headers, and the document shapes of
the upstream sources, and exits 1 on a finding.

Never silence a finding by loosening a rule. `THIRD-PARTY-NOTICES.md` has the policy, including the
four steps required *before* any third-party content could be added and why the example corpus
counts as original work.

The default scan covers the tracked tree; `--history` covers every blob in every commit. Deleting a
file does not remove it from the repository, so a finding from `--history` means rewriting history,
not deleting a file — which is why it is much cheaper to not commit the content in the first place.

## Conventions

1. **Tooling is single-file C#, never a shell script.** No `.sh`, `.ps1`, `.bat`, or `.py` — every
   tool must run on native Windows with only the .NET SDK. `scripts/Directory.Build.props` resets
   the production analyzer settings for these.
2. **State current truth only.** Documents do not narrate their own history; `git log` is the
   changelog. No "previously said X" footers, no dated verification stamps.
3. **American English** for identifiers, comments, and prose, except where an external standard
   specifies otherwise (MCP's `notifications/cancelled` stays as spelled).
4. **LF line endings, UTF-8**, enforced by `.gitattributes`.
5. Nothing is shared back to `flatrick/dotnet-mcp`, where this content originated. The extraction
   was one-way and final — never set up synchronization or copy files back.
