# Handoff — continuing the dotnet-knowledge build

Entry point for the next agent. Read [`../AGENTS.md`](../AGENTS.md) first, then this, then
[`design/mcp-tool-surface.md`](design/mcp-tool-surface.md).

## Where this came from

The corpus and the API-doc query logic were extracted from `flatrick/dotnet-mcp`, where they lived
as `examples/language-features/`, `scripts/api-docs-query.cs`, and four git submodules under
`external/`. The submodules were the reason for the split: they cost setup friction on every clone
and every git worktree, for content that was never *required* to build or test that project.

Nothing is shared back. `dotnet-mcp` still has its own copies at time of extraction; deleting them
there is a separate, later step and is **not** this repository's concern. Do not set up any
synchronization between the two — the extraction is one-way and final.

## What exists and is verified

| Thing | State |
|---|---|
| `examples/language-features/` | Complete authored corpus. The build matrix discovers every SDK-style C# library project under `CSharp/dotnet/` and every VB project under `VB.NET/`; `CorpusProjectDiscoveryTests` holds the exact expected list, and special and legacy projects remain explicit. |
| `examples/language-features/CSharp/csx/roslyn-5.6.0/` | Bundled eight-scenario, BCL-only C# script showcase. Every descriptor runs through the Roslyn 5.6.0 embedding API; five shared scenarios also run through the matching pinned `csi` host on Windows. The manifest/descriptor/file inventory and both Roslyn package pins are enforced. |
| `src/DotNetKnowledge.Mcp/` | Builds at 0 errors / 0 warnings. Serves `list_sources` over stdio. |
| `sources.json` | Five upstream sources with the commits `dotnet-mcp` had pinned. |
| `scripts/api-docs-query.cs` | The working XML-doc query logic, still in CLI form. Port it, don't rewrite it. |
| `scripts/generate-net48-examples.cs` | Legacy generator for deleted project roots; do not run it against the current tree. |
| `scripts/fetch-roslyn-wiki.cs` | A working blobless/sparse clone implementation — the reference for `sync_source`. |
| `tests/DotNetKnowledge.Corpus.Tests/` | Corpus build, isolated-compilation, runtime, runtime-marker, and C# script host/inventory contracts. |
| `scripts/install-corpus-test-sdks.cs` | Installs or verifies the exact corpus SDK matrix in a repository-private host. |

The server was smoke-tested over real stdio: `initialize` → `tools/call list_sources` returns the
pins, sync state, and cache directory. Reproduce with a redirected-process driver, not a shell pipe
— a Git Bash `>` redirect swallowed the server's stdout entirely and looked like a server fault.

## What to build next, in order

**1. `sync_source`.** Everything else is blocked on it. `scripts/fetch-roslyn-wiki.cs` already does
the exact clone this needs — blobless, `--no-checkout`, sparse, then checkout a commit — so port its
`Git()` helper rather than starting over. Honor `sources.json`'s `sparse` array. Report the resolved
commit. Expect the first `dotnet-api-docs` sync to be slow enough that a progress-free wait looks
like a hang; it is large enough that `du` timed out while measuring it.

**2. `lookup_api` and `search_api`.** Port `scripts/api-docs-query.cs`. It already resolves the
ECMA-XML layout of both doc repos and handles `Type` vs `Type.Member`. Two changes on the way in:
its submodule path probing is replaced by `SourceCache.DirectoryFor`, and `search_api` returns
names only — no bodies — so an agent can narrow cheaply.

**3. `list_examples` and `get_example`.** `MANIFEST.md` is the index. Parsing markdown tables at
runtime is the obvious approach and the wrong one: generate a JSON index from the manifest at build
time so a malformed table is a build failure rather than a silently short result set. The generated
shape must carry an example-kind field that distinguishes project examples from script scenarios;
script records also preserve their descriptor-backed hosts, support files, and behavior.

**4. `search_language_docs` / `get_language_doc`** over `csharplang` and `vblang`.

## Non-negotiables

These are correctness obligations, not preferences. The reasoning is in
[`design/mcp-tool-surface.md`](design/mcp-tool-surface.md).

- **Every payload carries the provenance envelope** — `repo`, `ref` (`pinned` / `head:<branch>` /
  `bundled`), `commit`, `fetchedAt`. The reason to prefer this server over a web search is that its
  answers are tied to a known revision; an unlabeled answer forfeits exactly that.
- **No query tool ever triggers a download.** It checks, and fails fast with an imperative remedy
  naming the tool to call. A partially-synced source answers with plausible absences that look like
  real "not found" results — the dangerous failure, because nothing about it looks like an error.
- **No silent truncation.** Every capped result set carries `isPartial` / a cursor.
- **`list_sources` keeps returning `cacheDir`.** Structured lookup will not cover everything — the
  corpus itself was built by grepping raw proposal trees — and an agent has no other way to find
  them. Removing it silently deletes a capability the tools do not replace.

## Facts already established — do not re-derive

- **The per-`<LangVersion>` trees are hand-authored probes.** The on-disk tree is current truth.
  `scripts/generate-net48-examples.cs` targets deleted project roots and is not a validation command
  for the current corpus.
- **C# scripts use host coordinates, not project coordinates.** The eight scenarios under
  `CSharp/csx/roslyn-5.6.0/` use only the BCL and are verified through
  `Microsoft.CodeAnalysis.CSharp.Scripting` 5.6.0; the five descriptors naming `csi` also run
  through `Microsoft.Net.Compilers.Toolset` 5.6.0 `tasks/net472/csi.exe` on Windows. The net10
  embedding executable is a host, not evidence that `.csx` is a .NET 10 file-based-program format.
  Its path restrictions are correctness boundaries, not a sandbox: scripts are trusted code with
  the process's permissions. The host requests cooperative cancellation after 30 seconds and on
  Ctrl+C; a script that does not observe cancellation may require caller process termination.
- **The legacy `CSharp_v7.0` project needs Visual Studio's `MSBuild.exe`.** Its current path is
  `examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v7.0/CSharp70.csproj`.
  `dotnet build` restores its `PackageReference` items and resolves none of them, because a non-SDK
  project consumes package assets through NuGet targets that ship with VS, not with the SDK. It
  fails with `CS0246` on `Span` and `ValueTask` and says nothing about the toolchain.
- **Applicable net48 C# projects need an explicit `Microsoft.CSharp` reference** for the C# 4.0
  `dynamic` row. That failure is `CS0656` at *emit*, so any earlier binding error in the project
  hides it entirely — a probe missing an unrelated reference will report it as absent.
- **Probe constructs in isolation.** A whole-project VB build reported 2 errors where per-folder
  builds reported 5. Neither compiler announces that it stopped early. This is why
  `scripts/verify-feature-floors.cs` compiles one row at a time, and why a VB floor derived from a
  whole-project build is not evidence.
- **Each VB family is one `src/` tree plus a project per pinned `<LangVersion>`.** VB prepends
  `RootNamespace` to every declaration, so every pinned project globs the same files and no VB
  source names a project. Edit under `src/`; there is no second copy. `MyType=Windows` is
  per-compilation and lives only in the net48 family's `my/` projects.
- **`MANIFEST.md`'s VB tables carry a Measured floor column**, giving the lowest pin at which each
  row compiles and the `verify-feature-floors.cs` evidence tier that floor rests on. Whatever
  generates the build-time example index must carry the column through rather than collapse it: a
  floor recorded from `sdk-pin` alone is a fact about the installed SDK and drifts.
- **Pinning an SDK-style project to `ISO-1`/`ISO-2`** always fails on the SDK's generated
  `AssemblyAttributes.cs`, whose `TargetFramework` attribute uses `global::`. Set
  `GenerateTargetFrameworkAttribute=false` or every C# 1.x era probe reports a phantom failure.
- **Corpus verification has three layers.** Project builds prove validity at a declared
  SDK/TFM/language coordinate; isolated compilation cases prove positive and negative feature
  boundaries; runtime cases prove comments that assert observable behavior. Project builds still
  require 0 errors and 0 warnings.
- **An older TFM does not select its historical compiler.** SDK 10 targeting an older TFM uses SDK
  10's compiler against the older reference pack. SDK, TFM, `LangVersion`, and runtime execution
  are independent case inputs.
- **Runtime-behavior claims require a canonical source marker.** Add
  `// Runtime verification: <case-id>` in C# or `' Runtime verification: <case-id>` in VB, and give
  that exact case a nonempty `runtimes` array. The marker must occur in the canonical source path
  named by the case.

## Open decisions

- **Whether the corpus ships inside the NuGet package or is fetched like the upstream sources.**
  It is ~7 MB, which packages acceptably and keeps first-run zero-setup. Bundling is assumed
  throughout the current design; revisit only if the package size becomes a real complaint.
- **Whether `dotnet-mcp` consumes this server.** Nothing in it reads the corpus today, so the
  extraction was subtractive. If it later wants the examples as test fixtures, it should consume
  this server or clone this repo — never copy files back.
- **Server test strategy.** The corpus tests do not cover the MCP server. `sync_source` and the
  manifest-index paths are the first server paths worth covering; both have failure modes that are
  silent rather than loud.

## Running it

```bash
dotnet build src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj
dotnet run --project src/DotNetKnowledge.Mcp    # stdio; expects a client on stdin
```

Install or verify the exact corpus test SDKs with
`dotnet scripts/install-corpus-test-sdks.cs`, then run the suite through the private host as shown
in [`../scripts/install-corpus-test-sdks.md`](../scripts/install-corpus-test-sdks.md). Do not repeat
the SDK setup manually.

Client registration:

```json
{
  "mcpServers": {
    "dotnet-knowledge": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/src/github/flatrick/dotnet-knowledge/src/DotNetKnowledge.Mcp"]
    }
  }
}
```

Override the cache location with `DOTNET_KNOWLEDGE_CACHE`; it otherwise defaults to
`%LOCALAPPDATA%\dotnet-knowledge\sources`.
