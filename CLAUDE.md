# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Orientation

Read [`AGENTS.md`](AGENTS.md) first; it carries the corpus rules and the reasoning behind them. This
file covers commands and the cross-file architecture. For current state, [`README.md`](README.md)
has the status summary and [`docs/backlog/`](docs/backlog/README.md) holds one file per known issue.

Before working on something that looks already-settled or surprisingly awkward, read
[`docs/decisions.md`](docs/decisions.md) and [`docs/gotchas.md`](docs/gotchas.md). They are
append-only and newest-first: decisions record what was rejected and why, gotchas record facts that
cost real time and are not inferable from the code.

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
dotnet build DotNetKnowledge.slnx                   # src/ and its tests; never the corpus
dotnet test DotNetKnowledge.slnx                    # ditto

# The installed server — what .mcp.json and .codex/config.toml launch
dotnet scripts/install-mcp-tool.cs                  # which build would launch next
dotnet scripts/install-mcp-tool.cs -- install       # pack + install/update the user-global tool
dotnet scripts/install-mcp-tool.cs -- uninstall     # remove it

# Dev tooling — single-file C# programs; arguments go after `--`
dotnet scripts/verify-no-vendored-content.cs                     # licensing guard; exit 1 on a finding
dotnet scripts/verify-no-vendored-content.cs -- --history        # same checks against every commit
dotnet scripts/verify-feature-floors.cs                          # classify every CSharp_v* group folder
dotnet scripts/verify-feature-floors.cs -- --project CSharp_v7.0 # one project
dotnet scripts/verify-feature-floors.cs -- --language vb         # the VB ladder, both families
dotnet scripts/verify-feature-floors.cs -- --language vb --project dotnet/Net10/15.5/library
dotnet scripts/verify-feature-floors.cs -- --language vb --project dotNetFramework/v4.8/latest/my
dotnet scripts/verify-feature-floors.cs -- --json                # machine-readable
dotnet scripts/verify-feature-floors.cs -- --offline             # skip the NuGet download
dotnet scripts/verify-project-namespaces.cs                      # namespace vs. RootNamespace drift
dotnet scripts/verify-project-namespaces.cs -- --json            # machine-readable
dotnet scripts/csharp-placement-sweep.cs -- --out .scratch/sweep.json   # ~3 min; needs VS MSBuild
dotnet scripts/render-floor-report.cs -- --sweep .scratch/sweep.json    # readable report
dotnet scripts/render-floor-report.cs -- --sweep .scratch/sweep.json --baseline .scratch/floors-cs.json
```

`csharp-placement-sweep.cs` walks the *whole* C# ladder rather than one rung down, which is what
produced `MANIFEST.md`'s **Lowest accepted `/langversion`** column; `render-floor-report.cs` turns
its JSON into prose, and takes `verify-feature-floors.cs -- --json` output as an optional contrast
baseline. Neither is a gate — they are measurements, run when the question comes up again.

`dotnet <file>.cs` silently claims some flags for itself, which is why every script takes its
arguments after `--`.

`--project` is spelled differently per language, because the two corpora are shaped differently.
C# takes a project directory name (`CSharp_v7.0`). VB takes the project directory's path relative to
`examples/language-features/VB.NET`, forward slashes, `<family>/<pin>/<kind>` —
`dotnet/Net10/15.5/library`, `dotNetFramework/v4.8/latest/my`.

**Testing `examples/` is a separate concern from testing `src/`, and the two solutions say so.**
`DotNetKnowledge.slnx` holds `src/` and its tests; `Corpus.slnx` holds the corpus suite and the
`csx` script host. Never put the corpus projects back into `DotNetKnowledge.slnx`: the two need
*different .NET hosts*, and a solution file cannot express that.

The corpus test suite is `tests/DotNetKnowledge.Corpus.Tests/`. Its exact SDK versions live in a
repository-private host. Install or verify them with
`dotnet scripts/install-corpus-test-sdks.cs`, then use the private-host command documented in
[`scripts/install-corpus-test-sdks.md`](scripts/install-corpus-test-sdks.md); do not repeat the SDK
setup manually.

**A plain `dotnet test` against the corpus suite is wrong, `Corpus.slnx` included** — the machine
host does not carry SDK 5.0.408 or 7.0.410 and never will, so the compiler-boundary cases skip and
the toolchain preflight fails. That failure reads as "those SDKs are not installed" while the real
cause is the host, which is why `RequiredToolchainsTests` now names the host it inspected.

Smoke-testing the server over stdio needs a redirected-process driver, not a shell pipe — a Git Bash
`>` redirect swallows the server's stdout entirely and looks like a server fault.

### Building corpus projects

SDK-style projects (`CSharp/dotnet/10/latest/*`, the VB projects, and three of the fourteen net48 C#
projects — `CSharp_v1.0-Unsafe`, `CSharp_v8.0`, `CSharp_v8.0-Unsafe`) build with `dotnet build`. The
other eleven net48 C# projects are **legacy non-SDK XML and need Visual Studio's `MSBuild.exe` on
Windows**:

```bash
# vswhere lives at "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe";
# scripts/verify-feature-floors.cs and CorpusProjectBuildTests locate MSBuild the same way.
vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe"
MSBuild.exe examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v7.0/CSharp70.csproj -t:Restore;Build
```

`dotnet build` on those restores their `PackageReference` items and then resolves none of them —
a non-SDK project consumes package assets through NuGet targets that ship with VS, not with the
SDK. It fails with `CS0246` on `Span` and `ValueTask` and says nothing about the toolchain, so it
reads as a broken sample.

`CorpusProjectBuildTests` builds both halves. The legacy half runs through `MSBuild.exe` and reports
inconclusive — naming the `vswhere` path it inspected — when Visual Studio is absent; every other
row runs through the repository-private host. `MSBuild.exe -v:minimal` prints no
`0 Warning(s)`/`0 Error(s)` summary at all, unlike `dotnet build -v:minimal`, so that half also
passes `-clp:Summary`.

## The corpus

C# layout is `<language>/<runtime>/<version>/<project>/<version-folder>/<feature-group>/`; VB's is
`<language>/<runtime>/<version>/src/<version-folder>/<feature-group>/` with projects beside `src/`:

```
examples/language-features/
  CSharp/dotnet/10/latest/{exe,library,unsafe}/        # SDK-style net10.0
  CSharp/dotNetFramework/v4.8/CSharp_v1.0 … CSharp_v8.0   # non-SDK net48, one pinned <LangVersion> each
  CSharp/CSharpComTypeLib/                            # support assembly for the NoPIA row, not a corpus project
  CSharp/CSharpRefReturnLib/                          # support assembly for the net48 VB ref-return row, not a corpus project
  VB.NET/dotnet/Net10/                                # src/ + a project per pin
  VB.NET/dotNetFramework/v4.8/                        # src/ + a project per pin, plus the my/ kind
  MANIFEST.md                                         # the index and completion oracle
```

**The VB half is laid out differently from the C# half.** Each VB family keeps one copy of every row
under `src/<version folder>/<group>/`, and a project per pinned `<LangVersion>` selects rows out of
it with explicit `Compile` globs:

```
examples/language-features/VB.NET/dotnet/Net10/
  Directory.Build.props
  src/Baseline/ src/Vb14/ src/Vb15/ src/Vb15_3/ src/Vb15_5/ src/Vb16_0/ src/Vb16_9/ src/Vb17_13/
  11/library/ 14/library/ 15/library/ 15.3/library/ 15.5/library/
  16/library/ 16.9/library/ 17.13/library/ latest/library/
examples/language-features/VB.NET/dotNetFramework/v4.8/
  Directory.Build.props
  src/…
  11/library/ … latest/library/
  11/my/  latest/my/
```

The pins are `11, 14, 15, 15.3, 15.5, 16, 16.9, 17.13, latest`. `vbc` rejects `17` and `17.0` with
`BC2014`, so those rungs do not exist. A project holds every row that compiles at its pin, including
rows filed above it whose feature `LangVersion` does not gate — the green build is what makes that
ungatedness a checked fact. Version folders under `src/` keep the `Vb15_3/` spelling because they
are namespace segments; project directories use the `15.3` spelling `vbc` accepts.

`src/` is per-family, not shared, because four rows genuinely diverge: `MyNamespaceHelpers` (net48
only), `ConsumingCSharpRefReturnValues` (both, different subject), and
`CallerArgumentExpressionConsumption` plus `OverloadResolutionPriorityConsumption` (net10 only).

Separate `*-Unsafe` and `exe` projects exist because `AllowUnsafeBlocks` and `OutputType` are
per-compilation switches that cannot be scoped to a folder; the mainline projects stay on default
compilation and those rows are housed apart. **VB's `MyType=Windows` is the same kind of switch and
gets the same treatment:** it lives only in the net48 family's `my/` projects, at the `11` and
`latest` rungs, and each of that family's nine `library.vbproj` files `Compile Remove`s the
`MyNamespaceHelpers` row. The net10 family's `src/` does not hold the row at all, so nothing there
needs removing.
`docs/design/language-feature-showcase-design.md` has the applicability rule and the rest of the
reasoning.

**`CSharp_v8.0`, `CSharpRefReturnLib` and the eleven net48 VB projects carry
`Microsoft.NETFramework.ReferenceAssemblies`** — the VB ones through their family's
`Directory.Build.props`. Those projects, and only those, build with no machine-installed .NET
Framework targeting pack. The net48 VB family project-references `CSharpRefReturnLib` for its
ref-return subject, which is why both halves need it. `CSharp_v1.0-Unsafe` and `CSharp_v8.0-Unsafe`
are SDK-style net48 too and carry nothing of the kind; the legacy non-SDK C# net48 projects stay
Windows-only regardless, because they need Visual Studio's `MSBuild.exe`.

### Rules that are load-bearing

- **Verification has three layers:** project builds prove validity at a declared SDK/TFM/language
  coordinate; isolated compilation cases prove positive and negative feature boundaries; runtime
  cases prove comments that assert observable behavior.
- **Every project build requires 0 errors AND 0 warnings.** `TreatWarningsAsErrors` and
  `MSBuildTreatWarningsAsErrors` are both inherited from the root `Directory.Build.props`
  specifically so this layer is mechanical.
  The pair is what makes the exit code sufficient: `TreatWarningsAsErrors` reaches the compilers and
  NuGet, `MSBuildTreatWarningsAsErrors` reaches MSBuild's own `MSB####` warnings, and without the
  second a build carrying one still exits 0.
  Never add a `#pragma warning disable` to get past a warning, and do not override either property
  in the corpus subtree.
  `scripts/Directory.Build.props` resets both, because a dev script is not gated content.
- **An older TFM does not select its historical compiler.** SDK 10 targeting an older TFM uses SDK
  10's compiler against the older reference pack. Keep SDK, TFM, `LangVersion`, and runtime
  execution as separate case inputs.
- **Every new runtime-behavior claim needs a source marker.** Add
  `// Runtime verification: <case-id>` in C# or `' Runtime verification: <case-id>` in VB to the
  canonical authored source, and give that exact case a nonempty `runtimes` array. The marker must
  occur in the canonical source path named by the case. Compilation cannot check "this is a view"
  or "this rounds to even".
- **A pinned `<LangVersion>` does not prove it either.** Roslyn enforces a version only where the
  binder calls `CheckFeatureAvailability`; syntax-driven features got that call, semantic and
  attribute-driven ones did not. `GeneralizedAsyncReturnTypes` (C# 7.0) compiles at
  `/langversion:5`, `Variance` (C# 2.0) at `ISO-1`. `scripts/verify-feature-floors.cs` settles these
  by escalating to compilers at a *native* ceiling — the in-box `%WINDIR%\Microsoft.NET\Framework64`
  csc for C# 2/3/5, `Microsoft.Net.Compilers` 1.3.2 for C# 6. C# 4 and C# 1.x have no compiler on a
  modern machine, so those floors report `UNPROVEN` rather than a guess. `MISPLACED`,
  `NOT-VERSION-SPECIFIC` and `UNDER-PLACED` fail the run; every other outcome is a finding about the
  toolchain. Every verdict also carries an `evidence` field — `native-ceiling`, `legacy-pin`,
  `sdk-pin`, `exempt`, `none` — because a floor settled by the installed SDK under `/langversion` is
  a fact about today's toolchain and drifts, while one settled at a native ceiling does not, and a
  row no compiler version could ever speak to is a third thing again.
- **The C# half measures a second, separate quantity, and `MANIFEST.md`'s two version columns must
  never be merged.** After classifying a row the probe walks the ladder *down* until a rung rejects
  it, and reports the lowest rung the installed compiler still accepts — `MANIFEST.md`'s C#
  **Lowest accepted `/langversion`** column, `LowestAcceptedLangVersion` in `--json`. VB's
  **Measured floor** is placement-derived (the lowest pin whose project compiles the row) and VB does
  not descend at all. The two disagree on exactly the rows the probe exists to find:
  `GeneralizedAsyncReturnTypes` is `UNGATED` at a native ceiling, so a real C# 6 compiler rejects it,
  while today's compiler accepts it at `/langversion:5`. The descent is `sdk-pin` evidence and can be
  nothing else — a period compiler has one fixed ceiling and cannot be walked down a ladder.
- **`UNDER-PLACED` is the converse of `MISPLACED`.** A project holds *every* row that compiles at
  its pin, so a row it could build and does not claim is as much a defect as one it claims and
  cannot build. The check compiles each unclaimed `src/` row at the pin; VB only, because C#
  projects own their rows on disk rather than globbing a shared tree.
- **`MISPLACED` means a stale project file, not a row filed above its pin.** A project deliberately
  holds every row that compiles at its pin, so sitting above the pin is the corpus's model rather
  than an error. The probe compiles such a row at the pin first: accepted, it goes through the normal
  floor probe with a note that it sits above the pin; rejected but accepted at its own version, it is
  `MISPLACED` — the project claims a row it cannot build. Rejected at both, it is `INCONCLUSIVE`,
  because the harness rather than the pin is what failed.
- **The VB ladder is probed with `-- --language vb`.** The escalation reaches native ceilings at
  VB 14 (`Microsoft.Net.Compilers` 1.3.2), VB 11 (`v4.0.30319`) and VB 9 (`v3.5`); VB 10 and VB 12
  report `UNPROVEN`, and nothing above VB 14 has a native ceiling at all. In practice that reaches
  exactly one row of this corpus — `ConsumingCSharpRefReturnValues`, `UNGATED` at `native-ceiling`.
  Every other VB floor rests on `sdk-pin` or on nothing.
- **Pinning any project to C# 1.x needs `GenerateTargetFrameworkAttribute=false`** — the generated
  `AssemblyAttributes.cs` uses `global::`, so every C# 1.x era probe otherwise reports a phantom
  `CS8022`. This is not an SDK-only hazard, and `ISO-1`/`ISO-2` are not the only spellings that hit
  it: `Microsoft.Common.CurrentVersion.targets` generates the same file for a legacy non-SDK
  project, which is how `CSharp_v1.0` sat broken while every probe of its rows reported clean.
- **Both net48 C# project families need an explicit `Microsoft.CSharp` reference** for the C# 4.0
  `dynamic` row. That failure is `CS0656` at *emit*, so any earlier binding error in the project
  hides it entirely.
- **Probe constructs in isolation.** A whole-project VB build reported 2 errors where per-folder
  builds reported 5; neither compiler announces that it stopped early.

### Current per-version project model

The per-`<LangVersion>` `CSharp_v*` projects are **hand-authored probes**, replacing the older
derived-projects model. Consequences:

- `scripts/generate-net48-examples.cs` still targets the deleted derived-project layout.
  **Do not run it against the new tree.** The `GENERATED-COMPILE-ITEMS` markers inside the
  `CSharp_v*` csproj files are inherited artifacts that nothing regenerates.
- Treat the on-disk tree as truth where older planning material or `MANIFEST.md` still describes the
  superseded model.
- Do not fan an edit across every copy of a sample by default — ask which projects are in scope.

**Every project's namespace names its own coordinate.** `RootNamespace` is
`Net<runtime>_<Language><LangVersion>_<Kind>` — `Net10_CSharp13_Library`, `Net48_CSharp7_1_Exe`,
`Net10_Vb15_3_Library`, `Net48_Vb11_My` — and
every C# file under a project declares that value as its first namespace segment, so an open file
says which project it belongs to. C#'s `RootNamespace` only seeds new-file templates and will not
enforce this, and the two drifted apart once already: six net10 library projects all declared
`CSharpNet10Latest`, ten net48 projects all declared `CSharpFw48Cs73`, and every build stayed green.
`dotnet scripts/verify-project-namespaces.cs` is what catches it; it also checks `<StartupObject>`,
whose failure mode is a bare `CS1555` that never mentions namespaces.

**VB is exempt from that rule and subject to its inverse.** A VB compilation prepends
`RootNamespace` to every declaration itself, so a `.vb` file declares a version-relative namespace
(`Namespace Vb15.Tuples`) and takes its project prefix at compile time — which is exactly what lets
every pinned project in a family glob the same `src/` tree. The same script therefore requires that
a `.vb` file under a family's `src/` must **not** begin its namespace with a `Net10_` or `Net48_`
prefix. Such a file compiles to a doubled namespace
(`Net10_Vb14_Library.Net10_Vb14_Library.Vb14.X`), and under a shared tree it corrupts every pin of
that family at once.

## The MCP server

`Program.cs` is a generic-host stdio server: `AddMcpServer().WithStdioServerTransport()
.WithToolsFromAssembly()`, with `SourceCatalog` (reads `sources.json`) and `SourceCache` (resolves
the on-disk cache) as singletons. Tools live in `Features/<Area>/<Name>Tool.cs` and are discovered by
assembly scan.

**stdout is the protocol channel.** Every log goes to stderr; anything else written to stdout
corrupts the session and surfaces as an opaque client-side parse error. Logging is configured for
this in `Program.cs` — do not add a console provider that writes to stdout.

Cache location defaults to `%LOCALAPPDATA%\dotnet-knowledge\sources` — the per-user *data*
directory elsewhere: `$XDG_DATA_HOME`/`~/.local/share` on Linux, `~/Library/Application Support` on
macOS — overridable with `DOTNET_KNOWLEDGE_CACHE`. It sits outside any repository so one download
serves every clone and worktree on the machine, and it is deliberately not the XDG *cache*
directory: a synced pin must survive cache cleaners (`docs/decisions.md`).

Implemented: `list_sources`, `sync_source`, `search_api`, `lookup_api`, `search_api_text`,
`find_api_references`, `search_language_docs`, `get_language_doc` and `get_language_doc_outline`.
Bundled-example queries are future work. The intended surface for all of them is in `docs/design/mcp-tool-surface.md`; known
defects are one file each in `docs/backlog/`.

`Text/DocumentationText.cs` is the one seam where documentation text is cleaned, and it has two
stages whose placement is load-bearing: **normalization at the read**, before anything matches, and
**budgeting at the payload**, after matching and paging. Putting either on the wrong side breaks a
guarantee — normalizing on the way out would make the text a search matched differ from the text it
returns, and budgeting before a match would drop every hit past the cap and report an absence.
`docs/design/mcp-tool-surface.md` has the reasoning.

### Non-negotiables for every tool

These are correctness obligations, not preferences:

- **Every payload carries the provenance envelope** — `repo`, `ref` (`pinned` / `head:<branch>` /
  `bundled`), `commit`, `fetchedAt`. Never absent, never inferred. The reason to prefer this server
  over a web search is that its answers are tied to a known revision.
- **No query tool ever triggers a download.** It checks, and fails fast with an imperative remedy
  naming the tool to call. A partially-synced source answers with plausible absences that look like
  real "not found" results — the dangerous failure, because nothing about it looks like an error.
- **No silent truncation.** Every capped result set carries `isPartial` or a cursor, and every
  capped *string* carries `isTruncated`. An agent that receives a quietly-truncated search concludes
  the symbol does not exist. Report truncation in a field, never by marking the text: an ellipsis
  cannot be told from one the source itself wrote.
- **`list_sources` keeps returning `cacheDir`.** Structured lookup will not cover everything — the
  corpus itself was built by grepping raw proposal trees — and an agent has no other way to find
  them.
- **Search tools return names and locations, never bodies.** `search_api` returns fully-qualified
  names; `search_language_docs` returns `path:line` hits. The agent then spends context on a single
  `lookup_api` or `get_language_doc`. `search_api_text` carries the matched prose because the match
  *is* the location — there is no line number inside an XML element to point at — but it is capped
  at 300 characters and names the owning symbol, so the follow-up call is still the way to read the
  entry.
- **Every subprocess this server starts redirects standard input.** Git blocks during process
  startup when it inherits a piped stdin handle that the parent is concurrently reading — not from
  a piped stdin alone, which is harmless and measured at 34 ms. An MCP stdio server always has that
  read pending, because the read *is* the JSON-RPC transport. `GitCommandRunner` redirects standard
  input; anything else that starts a process must too. `docs/gotchas.md` records the evidence.

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

## Continuous integration — configured, not running

**Actions is disabled for this repository. No workflow executes, and local runs are the only
verification there is.** Keep the workflow configuration current, and add a job when you add a
suite, so turning Actions on is a settings change rather than a project — but never treat a
workflow step as the thing that runs a test. Minutes cost money on a private repository, and while
this is a personal project that cost buys nothing a local run does not.

So: a commit on `main` carries no evidence that anything passed, because there is no run to read;
and a new suite needs an entry in the command list above, not only a workflow step, or nothing will
ever invoke it. [`docs/design/ci.md`](docs/design/ci.md) has the workflow's design and the runbook
for turning Actions on.

## Conventions

1. **Tooling is single-file C#, never a shell script.** No `.sh`, `.ps1`, `.bat`, or `.py` — every
   tool must run on native Windows with only the .NET SDK. `scripts/Directory.Build.props` resets
   the production analyzer settings for these.
2. **State current truth only.** Documents do not narrate their own history; `git log` is the
   changelog. No "previously said X" footers, no dated verification stamps.
   [`docs/decisions.md`](docs/decisions.md) and [`docs/gotchas.md`](docs/gotchas.md) are the two
   deliberate exceptions: they are append-only, dated, and never edited, because a superseded entry
   is what stops a settled question being reopened. Do not tidy them.
3. **American English** for identifiers, comments, and prose, except where an external standard
   specifies otherwise (MCP's `notifications/cancelled` stays as spelled).
4. **LF line endings, UTF-8**, enforced by `.gitattributes`.
5. Nothing is shared back to `flatrick/dotnet-mcp`, where this content originated. The extraction
   was one-way and final — never set up synchronization or copy files back.
6. **`.scratch/` at the repository root is the scratch folder**, gitignored and never committed. Any
   throwaway working file belongs there — build and test logs, diagnostic scripts, probe programs,
   intermediate data — not logs alone, and in preference to any session or temporary directory
   outside the repository. Anything that must survive or reach another machine is not scratch:
   tracked diagnostic tooling goes in [`scripts/probes/`](scripts/probes/README.md).
