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
| `examples/language-features/` | Complete corpus, 449 example files across 7 projects. Copied verbatim. |
| `src/DotNetKnowledge.Mcp/` | Builds at 0 errors / 0 warnings. Serves `list_sources` over stdio. |
| `sources.json` | Five upstream sources with the commits `dotnet-mcp` had pinned. |
| `scripts/api-docs-query.cs` | The working XML-doc query logic, still in CLI form. Port it, don't rewrite it. |
| `scripts/generate-net48-examples.cs` | The corpus generator, with its `--check` drift mode. |
| `scripts/fetch-roslyn-wiki.cs` | A working blobless/sparse clone implementation — the reference for `sync_source`. |

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
time so a malformed table is a build failure rather than a silently short result set.

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

- **`CSharpFw48Cs73` needs Visual Studio's `MSBuild.exe`.** `dotnet build` restores its
  `PackageReference` items and resolves none of them, because a non-SDK project consumes package
  assets through NuGet targets that ship with VS, not with the SDK. It fails with `CS0246` on `Span`
  and `ValueTask` and says nothing about the toolchain.
- **Both net48 C# projects need an explicit `Microsoft.CSharp` reference** for the C# 4.0 `dynamic`
  row. That failure is `CS0656` at *emit*, so any earlier binding error in the project hides it
  entirely — a probe missing an unrelated reference will report it as absent.
- **Probe constructs in isolation.** A whole-project VB build reported 2 errors where per-folder
  builds reported 5. Neither compiler announces that it stopped early.
- **Pinning an SDK-style project to `ISO-1`/`ISO-2`** always fails on the SDK's generated
  `AssemblyAttributes.cs`, whose `TargetFramework` attribute uses `global::`. Set
  `GenerateTargetFrameworkAttribute=false` or every C# 1.x era probe reports a phantom failure.
- **The corpus has no test suite and needs none.** Its gate is 0 errors and 0 warnings per project.

## Open decisions

- **Whether the corpus ships inside the NuGet package or is fetched like the upstream sources.**
  It is ~7 MB, which packages acceptably and keeps first-run zero-setup. Bundling is assumed
  throughout the current design; revisit only if the package size becomes a real complaint.
- **Whether `dotnet-mcp` consumes this server.** Nothing in it reads the corpus today, so the
  extraction was subtractive. If it later wants the examples as test fixtures, it should consume
  this server or clone this repo — never copy files back.
- **Test strategy.** No tests exist yet. The `sync_source` and manifest-index paths are the two
  worth covering first; both have failure modes that are silent rather than loud.

## Running it

```bash
dotnet build src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj
dotnet run --project src/DotNetKnowledge.Mcp    # stdio; expects a client on stdin
```

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
