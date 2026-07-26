# Design — the dotnet-knowledge MCP server

## What it is for

An agent working on .NET code needs three kinds of reference material, and getting any of them from
a web search risks an answer describing a different version than the one in the project:

- **API shape** — signatures, overloads, parameter names, doc summaries for Roslyn and the BCL.
- **Language design** — proposals, specification text, and LDM notes for C# and VB.NET.
- **Worked examples** — what a given feature actually looks like at a given language version and TFM.

This server provides all three locally and states which revision each answer came from.

## Two classes of source, two lifecycles

| Class | What | Sync needed |
|---|---|---|
| **Bundled** | `examples/language-features/` — authored here, ships with the server | No |
| **Fetched** | The five entries in `sources.json` — upstream Microsoft repositories | Yes, explicitly |

The distinction matters for first-run experience: the corpus tools work immediately after install,
and only the doc tools require a sync.

## Tool surface

```
── sources (fetched only) ────────────────────────────────────────────
list_sources()
    → per source: name, purpose, pinned commit, currently-synced ref and
      commit, synced?, sizeOnDisk, cacheDir

sync_source(name, ref?)
    → clones or fetches into the cache. Long-running by nature.
      ref omitted  → the commit in sources.json (the vouched-for pin)
      ref: "head"  → the branch named in sources.json — opts into drift
    → returns the resolved commit and the on-disk path

── API docs ──────────────────────────────────────────────────────────
lookup_api(symbol, source?)
    symbol: "SymbolFinder" | "SymbolFinder.FindCallersAsync"
    → signatures, parameters, returns, summary, remarks

search_api(pattern, limit?, cursor?)
    → candidate fully-qualified names ONLY, no bodies

── language design docs ──────────────────────────────────────────────
search_language_docs(query, source?, limit?, cursor?)
    → path + line hits, no file bodies

get_language_doc(path, source)
    → the document's contents

── examples (bundled) ────────────────────────────────────────────────
list_examples(language?, version?, feature?)
    → manifest rows: version, feature, group folder, target projects,
      exclusion reasons

get_example(group, project?)
    → source text; project selects the TFM/format twin and defaults to the
      net10 one, listing the alternatives available
```

### Why the search tools return names and locations rather than content

An agent pays for every token it receives. `search_api` returning fully-qualified names lets it
narrow for almost nothing and then spend context on a single `lookup_api`. The same reasoning makes
`search_language_docs` return `path:line` hits rather than matched files: the agent decides what is
worth reading. A search tool that returns bodies turns one imprecise query into an unaffordable
response.

## The provenance envelope

Every payload — from every tool, including cached and bundled ones — carries:

```json
"source": {
  "repo": "dotnet/csharplang",
  "ref": "pinned",
  "commit": "36796924c898eb698d983b921001fa00cf689d9b",
  "fetchedAt": "2026-07-26T09:12:44Z"
}
```

`ref` is `"pinned"`, `"head:<branch>"`, or `"bundled"` for the corpus. It is never absent and never
inferred.

**This is the server's central correctness obligation, not a nicety.** The reason to prefer a local
lookup over a web search is that the answer is tied to a known revision. Once a caller can request
`head`, an unlabeled answer forfeits exactly that property — it becomes a web search with worse
coverage. An agent will not consult a sidecar or a log, so provenance travels in-band with the data
it describes.

## Sync is explicit, never implicit

A query tool must **never** trigger a download. `dotnet-api-docs` is large enough that a first-run
clone is measured in minutes, which means:

- an implicit sync inside a query blows the client's tool timeout, and
- a partially-completed one returns thin results that look like real answers — a symbol "not found"
  because its file has not arrived yet.

The second failure is the dangerous one, because nothing about it looks like an error. So: query
tools check for the source and fail fast, and the failure message names the remedy in imperative
form, because the reader is an LLM.

```json
{
  "error": "source_not_synced",
  "message": "csharplang is not synced. Call sync_source(name: \"csharplang\") first.",
  "source": "csharplang"
}
```

## The cache lives outside any repository

Default location, overridable by config:

- Windows — `%LOCALAPPDATA%\dotnet-knowledge\sources\<name>\`
- otherwise — `$XDG_CACHE_HOME/dotnet-knowledge/sources/<name>/` (or `~/.cache/...`)

One download then serves every repository and every git worktree on the machine. That is the
concrete advantage over the git-submodule arrangement this replaces, where the content was
per-clone and every new worktree needed its own `git submodule update --init`.

**`list_sources` must return `cacheDir`.** Structured lookup will not cover every need — the corpus
itself was sourced by grepping the raw proposal trees — and an agent has no other way to learn where
those trees are. Returning the path preserves `rg` as an escape hatch instead of quietly removing a
capability the tools do not replace.

## Fetching

Use a blobless, sparse, single-branch clone; the `sparse` array in `sources.json` names the paths
each source's tools actually read. `scripts/fetch-roslyn-wiki.cs` already does exactly this and is
the working reference implementation:

```
git clone --filter=blob:none --no-checkout --sparse <url> <dir>
git -C <dir> sparse-checkout set <paths...>
git -C <dir> checkout <pin-or-head>
```

## Naming, against dotnet-mcp

Agents commonly load both servers. The split is clean — `dotnet-mcp` analyses *the user's solution*,
this serves *reference material* — but the names must not blur it. `find_symbols` (their code) beside
`search_api` (the BCL) reads fine. Watch `find_symbol_source` against `lookup_api`: both can be read
as "where is this defined". Keep `api` in every tool name that answers about the BCL or Roslyn.
