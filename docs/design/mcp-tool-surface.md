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
    → detail: "signatures" | "full" per match, because the tier is decided
      per source — one source resolving the string as a type must not
      collapse another source's member match, and a signatures-only
      answer is otherwise indistinguishable from a signatures-only
      decision
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
    → namespaces match on complete segments, type names on any substring
    → matchedOn: "fullName" | "type" | "namespace" — so "every type in
      this namespace" is distinguishable from "types whose name contains
      this"
search_api_text(query, source?, limit?, cursor?)
    query: a literal, case-insensitive substring — no regex, see
      "Searching API documentation text" below
    → searches summary, remarks, returns, value, param, typeparam and
      exception text, matched AFTER reference elements are resolved, so
      what was searched is what comes back
    → hits: the owning symbol ("Type" or "Type.Member"), which element
      matched ("summary", "param:name", …), and the matched text capped
      at 300 characters with isTruncated stating it — never bodies
    → limit: 1-100, default 20

find_api_references(symbol, kind?, exact?, source?, limit?, cursor?)
    symbol: a fully-qualified TYPE name — the thing being used
    kind: "parameter" | "return" | "base" | "interface"; omit for all
    exact: true for declarations naming the type itself, false for ones
      naming an expression parameterized by it; omit for both
    → declarations that use the type structurally, matched inside
      compound expressions: string[], out string, IEnumerable<string>
    → hits: owning symbol, kind, parameterName, the type expression as
      declared, isExact, and the C# signature
    → totals: per-kind counts over the WHOLE result set, not the page
    → limit: 1-100, default 20

── language design docs ──────────────────────────────────────────────
search_language_docs(query, regex?, source?, limit?, cursor?)
    query: a literal substring; regex: true switches to full .NET
      regex, evaluated with the non-backtracking engine so no
      caller-supplied pattern can stall the server
    → hits: path, line, the matched line's text (capped at 300 with
      isTruncated stating it, as everywhere else), and a
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

The layout keeps this cheap. Namespace directories are flat — one directory per complete namespace
(`xml/System.Text.Json/`), never nested — so a namespace segment, a run of segments, and a whole-name
match are all operations on a directory name already in hand.

**The two sides of a name match differently, and deliberately.** A type name matches on any
substring, because `Concurrent` must keep finding `ConcurrentDictionary`. A namespace matches only on
complete dot-separated segments, because the alternative has an unacceptable blast radius: a raw
`Contains` over the joined name makes `search_api("Json")` mean "every type in `System.Text.Json`" as
well as "types named `*Json*`", and `Jso` mean it too, against a limit of 100. Whole-segment matching
keeps `Json` naming the namespace — a question worth answering — while `Jso` stays what it almost
certainly was, a type-name fragment.

Even so, `Json` legitimately matches both ways, which is why every item carries `matchedOn`. An agent
that asked for a type name and received a namespace's entire contents has been answered a question it
did not ask, and it cannot tell without being told. `fullName` outranks `type`, which outranks
`namespace`, so the most specific reading an item supports is the one reported; a multi-segment run
reaching the type name is `fullName`, while a single segment equal to the type name is just `type`
spelled exactly.

### Searching API documentation text

"Which API mentions this behavior?" is the question an agent asks when it knows what it needs and not
what it is called, and it is the one shape `lookup_api` structurally cannot serve, because it takes
the name as input. `search_api_text` answers it.

Text-searching `cacheDir` also answers it, and that remains a reason `list_sources` returns the path.
It is not a substitute: the escape hatch assumes the caller can run a process on the machine holding
the cache, which is true of a local coding agent and false of a sandboxed one or of any client
talking to a server hosted elsewhere.

**Every documented element is searchable**, remarks included. Leaving the largest and least specific
text out would keep responses smaller and would answer "no" to questions whose answer is in the
corpus — a plausible absence, which this server treats as the dangerous failure. Noise is handled
where it does no harm instead: each hit names the element it matched, so a caller can tell a summary
hit from a remarks hit and narrow without a second round trip.

**Matching runs on rendered text, not on the raw file.** A phrase like "value into a System.String"
exists only once a `<see cref>` has been resolved; in the file, an element sits in the middle of it.
Searching the raw XML would miss it, and would also match attribute noise no reader ever sees.

That forces the scan to be a two-phase one, because parsing 460 MB of XML per query is not
affordable and reading it is: a parallel raw-text prefilter rejects almost every file, and only
survivors are parsed and rendered. The prefilter tests **the longest whitespace-delimited token of
the query**, never the whole query, and that is a correctness requirement rather than a heuristic.
Every word of the rendered text comes from somewhere in the raw file — a text node copied verbatim,
or the attribute a rendered symbol name is built from — so a single token is a sound superset, while
the whole phrase is not.

**This is also why the tool takes a literal substring and not a regex**, unlike
`search_language_docs`. No cheap prefilter is a sound superset of an arbitrary pattern, so regex
would mean parsing the entire corpus per query or silently missing matches, and a search tool that
silently misses is the failure mode this server exists to avoid.

Measured on the pinned corpus: a full scan of 11,359 files costs about 0.5-0.7 s in parallel against
2.1 s serially, and a query answers end to end through the MCP host in roughly 0.7-3.6 s depending on
how many files survive to be parsed. No index is needed, and adding one would put a build step
between a sync and a correct answer.

Hits are deduplicated on symbol, element and text. Overloads each carry their own `Docs` under one
`MemberName`, so identical prose on `Create(a)` and `Create(a, b)` would otherwise arrive as
repeated hits a caller cannot tell apart; prose that genuinely differs between overloads survives,
because the text is part of the key.

### Structural references are a different question from prose

`search_api_text` answers "which docs mention this type". `find_api_references` answers "which
declarations use it" — a parameter, a return, a base class, an interface list. Measured on the
pinned corpus, the two differ by an order of magnitude for a popular type: `System.String` has
roughly 2,000 prose references and over 18,000 structural ones. Merging them would produce a result
serving neither question.

**Matching is on type-name boundaries, not equality and not substring.** A parameter is far more
often `System.String[]`, `System.String&`, or `IEnumerable<System.String>` than a bare
`System.String`, so equality would miss every `params string[]` and every `out string` — absences
that read as facts. A plain substring test would instead match `System.StringComparer`. The
occurrence therefore has to sit on boundaries: not preceded or followed by a character that
continues an identifier, a dotted path, or a `+` nested-type separator.

The prefilter can be the whole symbol here, unlike the prose search. A structural reference spells
the type out in an attribute or element with no rendering step in between, so the raw file text is
guaranteed to contain it.

**`kind` says where a reference sits, not what the type is to it.** A class implementing
`IComparer<string>` is an `interface` hit for `System.String`. `isExact` carries that distinction —
true when the declaration names the type itself, false when it names an expression parameterized by
it — and `exact` filters on it, because "what derives from `Stream`" and "what has a base
parameterized by `Stream`" are different questions. `typeExpression` still carries the expression as
declared, so a caller can see *how* it was parameterized.

**Totals cover the whole result set, before `kind` narrows it.** A widely-used type has tens of
thousands of references, and paginating them twenty at a time is a way of not saying so; a caller
asking only about parameters can still see that five hundred types implement the interface.

Query cost is 0.7-1.8 s against the whole corpus, so this needs no index either.

### Text is normalized at the read, and budgeted at the payload

`DocumentationText` is one seam with two stages, and which stage a rule belongs to is not a matter
of taste.

**Normalization runs where text is read from a source**, before anything matches against it:
references resolved, the source's own line wrapping folded away, `"To be added."` recognized as the
placeholder it is. It has to run there, because `search_api_text` matches the same string it later
returns — that is the only reason it can find a phrase spanning a resolved reference. Normalizing on
the way out instead would make the text searched and the text shown two different strings, which is
the defect the reference renderer was written to fix, reintroduced one layer up.

**Budgeting runs at the payload**, after matching and paging. It has to run there for the mirror
reason: capping text before a match would drop every hit past the cap and report nothing, a
plausible absence rather than a visible failure.

Two consequences worth stating, because both were inconsistencies before the seam existed:

- **A reference renders the same regardless of how upstream wrote it.** ECMA XML's
  `<see cref="T:System.String"/>` and MSDocs markdown's `<xref:System.String>` both become
  `System.String`, which is also what `lookup_api` accepts back. The xref form carries presentation
  the symbol does not — a `?displayProperty=` query, a `*` naming an overload group, percent-encoded
  punctuation — and all of it is stripped.
- **Truncation is reported, never marked.** One budget, one contract: the text is cut and
  `isTruncated` says so. An ellipsis in the text cannot be told from one the source itself wrote,
  and forces a caller to parse prose to learn a fact the payload should carry.

Whitespace folding is skipped for markdown-bodied documentation, where line structure is content:
collapsing it would run a fenced code block onto one line. That is why the rule is a parameter of
normalization rather than a property of it.

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
each source's tools actually read. `src/DotNetKnowledge.Mcp/Sources/SourceSynchronizer.cs` implements
exactly this:

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
