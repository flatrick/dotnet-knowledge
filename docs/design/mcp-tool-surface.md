# Design — the dotnet-knowledge MCP server

## What it is for

An agent working on .NET code needs three kinds of reference material, and getting any of them from
a web search risks an answer describing a different version than the one in the project:

- **API shape** — signatures, overloads, parameter names, doc summaries for Roslyn and the BCL.
- **Language design** — proposals, specification text, and LDM notes for C# and VB.NET.
- **Worked examples** — what a given feature actually looks like at a given language version and TFM.

This server provides all three locally and states which revision each answer came from.

The source and API-doc tools are implemented; language-design and bundled-example tools below are
the remaining surface. This document describes the intended surface throughout;
[`docs/backlog/`](../backlog/README.md) records deferred work against it.

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
      commit, fetchedAt, synced?, cacheDir

sync_source(name, ref?)
    → clones or fetches into the cache. Long-running by nature.
      ref omitted  → the commit in sources.json (the vouched-for pin)
      ref: "head"  → the branch named in sources.json — opts into drift
    → returns the resolved commit and the on-disk path

── API docs ──────────────────────────────────────────────────────────
lookup_api(symbol, source?, limit?, cursor?)
    symbol: "SymbolFinder" | "SymbolFinder.FindCallersAsync"
    → a bare type name returns each matching member's name and signature
      only, across every matching type; naming a member ("Type.Member")
      returns that member's full documentation — summary, parameters,
      returns, and remarks
    → limit: 1-100, default 20, over one flat member sequence across all
      matched types; cursor: opaque, bound to the symbol and to the
      searched sources' revisions, so a cursor from before a
      re-synchronization is rejected rather than silently misread
    → resolvedTypeNames lists every type the symbol matched, so a caller
      can see the full matched set even when a page shows only some of
      their members

search_api(pattern, limit?, cursor?)
    → candidate fully-qualified names ONLY, no bodies
    → pattern matches anywhere in the fully-qualified name: the whole
      name ("System.Text.Json.JsonSerializer"), a namespace segment in
      the middle of one ("Text" or "Json" in System.Text.Json.*), a
      namespace prefix, or the type name alone
    → each item states which part matched, so "every type in this
      namespace" is distinguishable from "types whose name contains this"
    ! today the pattern is matched against the type name alone, never
      the namespace — see "What search_api matches" below

── language design docs ──────────────────────────────────────────────
search_language_docs(query, regex?, source?, limit?, cursor?)
    query: a literal substring; regex: true switches to full .NET
      regex, evaluated with the non-backtracking engine so no
      caller-supplied pattern can stall the server
    → hits: path, line, the matched line's text (length-capped), and a
      server-issued section heading path — no file bodies
    → searches every markdown source the tool supports (csharplang,
      vblang, and roslyn-wiki today; the supported set is configuration,
      not code — see the "markdown" field in sources.json); source
      restricts
    → limit: 1-100, default 20

get_language_doc(path, source, section?, limit?, cursor?)
    section: a heading path exactly as issued by a search hit or an
      outline entry ("Metadata > Ref fields", disambiguated when a
      heading text repeats) — callers round-trip it, never construct it
    → with section: that complete heading section, paged only when it
      is genuinely large
    → without: the whole document, paged from the top
    → no size cap and no refusal; every page states whether more
      remains and carries the cursor for the next one
    → limit is a character budget, not an item count: 1000-50000,
      default 8000, snapped to a line boundary and never splitting
      a fenced code block or a table

get_language_doc_outline(path, source, limit?, cursor?)
    → limit: 1-500, default 100
    → the document's heading tree with section IDs, no bodies — the
      map an agent reads before spending context on content

── examples (bundled) ────────────────────────────────────────────────
list_examples(kind?, language?, version?, feature?)
    kind: "project" | "script"
    → project manifest rows: version, feature, group folder, target projects,
      exclusion reasons
    → script manifest rows: scenario ID, Roslyn version, entry, applicable hosts,
      demonstrates, note

get_example(id, kind?, project?)
    → project example: source text; project selects the TFM/format twin and
      defaults to the net10 one, listing the alternatives available
    → script example: kind: "script", language: "C#",
      host: { name: "Roslyn", version: "5.6.0" }, entry, supportFiles,
      applicableHosts, and descriptor-backed behavior expectations
```

The example tools are a future server surface. Their build-time index must preserve the example
kind so a script scenario is never presented as a project/TFM feature row. For scripts,
`scenario.json` supplies the support-file list, applicable hosts, and verified behavior; the
dedicated `MANIFEST.md` table supplies discovery metadata and must agree with the descriptor.

### Why the search tools return names and locations rather than content

An agent pays for every token it receives. `search_api` returning fully-qualified names lets it
narrow for almost nothing and then spend context on a single `lookup_api`. The same reasoning makes
`search_language_docs` return `path:line` hits plus the single matched line rather than matched
files: one line per hit is the triage budget, and the agent decides what is worth reading. A search
tool that returns bodies turns one imprecise query into an unaffordable response.

### What `search_api` matches

An agent looking for an API arrives holding one of several things, and all of them are the same
question: a fully-qualified name copied from a compiler error, a namespace it wants the contents of,
a fragment it half-remembers from the middle of a namespace path, or a bare type name. `search_api`
must answer all four, because the caller cannot know in advance which kind of string it is holding —
and a search tool that silently returns nothing for one of them is worse than one that refuses, since
an empty result set reads as "no such API".

Listing a *type's* members is already covered: `lookup_api` with a bare type name returns every
member's signature. The gap is namespaces.

The implementation matches the pattern against the type name alone. `ReadSearchSource` enumerates
each namespace directory and tests the file stem, composing the namespace into the result only
afterwards, so `search_api("System.Collections.Concurrent")` returns nothing while
`search_api("ConcurrentDictionary")` returns the type. The tool's description states this constraint,
which keeps the behavior honest but does not make it sufficient.

The layout makes the fix cheap. Namespace directories are flat — one directory per complete namespace
(`xml/System.Text.Json/`), never nested — so a namespace segment, a prefix, and a whole-name match are
all string operations on a directory name that is already in hand.

The question to settle before writing that code is blast radius, not feasibility. Matching a raw
substring against the composed name means `search_api("Json")` stops meaning "types named `*Json*`"
and starts also meaning "every type in `System.Text.Json`" — a much larger set, against a limit of
100, where the caller cannot see why any given item matched. Two constraints follow: matching should
be segment-aware rather than a plain `Contains` over the joined string, and each item should carry
what it matched on. An agent that asked for a type name and received a namespace's entire contents
has been answered a question it did not ask.

### Sections are the retrieval unit for language docs

A line range returns what the caller guessed; a heading section — the heading and everything until
the next heading of the same or higher level — is complete by construction. Section IDs are
therefore heading paths issued by the server and round-tripped verbatim, in the same spirit as
cursors. Sections are also the floor: markdown gives a paragraph no identity, so paragraph-level
IDs would be synthetic and churn with every upstream edit. Heading extraction uses Markdig's AST
rather than hand-rolled scanning, because a heading is only a heading outside a code fence and both
ATX and setext forms occur in these repositories.

These tools must stand alone for a sandboxed agent. The `cacheDir` escape hatch assumes the caller
can reach the per-user cache with its own tools; an agent confined to its workspace cannot, and for
it a structured search that falls short has no fallback.

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

When one response contains matches from both API repositories, each match carries its own source
envelope; provenance is never collapsed into one ambiguous top-level revision.

`list_sources` does not recursively measure cache size. The largest source is expensive enough that
walking it can make this cheap status call look hung; synchronization state and provenance are the
contract that query correctness depends on.

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

An API query with no `source` restriction requires both API-doc repositories to be synchronized.
It never searches only the available subset, because an incomplete result set looks authoritative.

```json
{
  "error": "source_not_synced",
  "message": "csharplang is not synced. Call sync_source(name: \"csharplang\") first.",
  "source": "csharplang"
}
```

## The cache lives outside any repository

Default location, overridable with `DOTNET_KNOWLEDGE_CACHE`:

- Windows — `%LOCALAPPDATA%\dotnet-knowledge\sources\<name>\`
- Linux — `$XDG_DATA_HOME/dotnet-knowledge/sources/<name>/` (or `~/.local/share/...`)
- macOS — `~/Library/Application Support/dotnet-knowledge/sources/<name>/`

The user *data* directory, not the XDG cache directory, deliberately: a synced pin must survive
cache cleaners, because query tools treat an absent source as "call `sync_source`", never as
something to refetch silently ([`docs/decisions.md`](../decisions.md)).

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
git -C <dir> fetch --depth 1 origin <pin-or-head>
git -C <dir> checkout --detach FETCH_HEAD
```

## Naming, against dotnet-mcp

Agents commonly load both servers. The split is clean — `dotnet-mcp` analyses *the user's solution*,
this serves *reference material* — but the names must not blur it. `find_symbols` (their code) beside
`search_api` (the BCL) reads fine. Watch `find_symbol_source` against `lookup_api`: both can be read
as "where is this defined". Keep `api` in every tool name that answers about the BCL or Roslyn.
