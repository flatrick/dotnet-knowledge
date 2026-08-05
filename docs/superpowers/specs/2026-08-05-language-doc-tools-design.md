# Language-Doc Query Tools Design

## Purpose

Implement the three language-design query tools from `docs/design/mcp-tool-surface.md`:
`search_language_docs`, `get_language_doc`, and `get_language_doc_outline`. These are the last
unimplemented tools on that surface; `list_sources`, `sync_source`, `search_api`, and `lookup_api`
already work under a client. Sections — a heading and everything until the next heading of the same
or higher level — are the retrieval unit, addressed by server-issued heading paths, per the standing
decision in `docs/decisions.md`. This spec settles what that decision left open: how a path
disambiguates, how each tool pages, the error taxonomy, and where the Markdig-based parsing lives.

## Scope

Three tools, one new library project, one new repository-root solution file. Excludes the bundled
example-corpus tools (`list_examples`, `get_example`), which remain future work untouched by this
change.

## Architecture: a standalone markdown library

`src/DotNetKnowledge.Markdown/DotNetKnowledge.Markdown.csproj` — a class library with a single
package reference, Markdig (latest stable at implementation time, resolved with
`dotnet add package`). It depends on nothing else in this repository: no `SourceCache`, no MCP
types, no JSON. Input is markdown text; output is plain data. That is what makes it reusable outside
this server, and it is unit-tested against literal strings with no git, no cache, and no network.

Public surface, namespace `DotNetKnowledge.Markdown`:

- `MarkdownHeading(int Level, string Text, string Path, int StartLine, int EndLine)` — one heading.
  `StartLine` is the heading's own line (1-based); `EndLine` is exclusive, the line the next
  same-or-higher heading starts on, or one past the last line of the file.
- `MarkdownOutline.Extract(string markdown) -> IReadOnlyList<MarkdownHeading>` — walks the Markdig
  AST for ATX and setext headings (a heading is only a heading outside a code fence, and both forms
  occur in these repositories, per the existing decision), computing each one's `Path` as its
  ancestor chain joined by `" > "`, disambiguated per the rule below.
- `MarkdownAtomicBlocks.Find(string markdown) -> IReadOnlyList<(int StartLine, int EndLine)>` —
  inclusive line ranges of fenced code blocks and pipe tables, read from the same AST.
- `MarkdownPager.Page(IReadOnlyList<string> lines, IReadOnlyList<(int Start, int End)> atomicBlocks, int startLine, int charBudget) -> (int EndLineExclusive, bool IsPartial)`
  — the paging algorithm described below.
- `MarkdownLineSearch.Search(string markdown, IReadOnlyList<MarkdownHeading> outline, string pattern, bool regex) -> IReadOnlyList<MarkdownLineHit>`,
  `MarkdownLineHit(int Line, string Text, string SectionPath)`. Regex mode constructs
  `new Regex(pattern, RegexOptions.NonBacktracking)`; a pattern that fails to construct under that
  engine (unsupported construct, e.g. a backreference, or a plain syntax error) throws from the
  `Regex` constructor exactly as `System.Text.RegularExpressions` already defines, and is not wrapped
  in a library-specific exception type — the MCP layer catches `ArgumentException` /
  `NotSupportedException` at that call site the same way it already catches framework exceptions
  elsewhere.

`src/DotNetKnowledge.Mcp/Features/LanguageDocs/` holds the MCP-shaped layer, mirroring
`Features/ApiDocs/`: `LanguageDocsModels.cs` (response records, mirroring `SourceProvenance` reuse),
`LanguageDocsQueryService.cs` (composes `DotNetKnowledge.Markdown` with `SourceSynchronizer` for
sync-checking, the cursor scheme, and provenance assembly), `LanguageDocsTool.cs` (the three
`[McpServerTool]` methods and JSON error shaping, following `ApiDocsTool`'s exception-to-JSON
pattern exactly).

Both new projects join the projects already listed in the new repository-root `DotNetKnowledge.slnx`
(`src/DotNetKnowledge.Mcp` and its two test projects, plus `DotNetKnowledge.Corpus.Tests`) once they
exist. The example corpus's own per-project `.slnx` files are untouched; this solution is the
server-side projects only, not the feature lattice.

## Section-path disambiguation

A heading's `Path` is its ancestor chain, each segment the heading's rendered plain inline text
(markdown emphasis/links resolved to their text content), joined by `" > "` — `"Metadata > Ref
fields"`. Content before a document's first heading has no section path; it is reachable only
through a whole-document (`section` omitted) fetch.

Collisions are resolved per document, not per level: while extracting, the server tracks how many
headings have produced each exact `Path` string so far. The first occurrence is untouched; the
second and later append `" (2)"`, `" (3)"`, and so on, to the colliding path. A document with two
sibling `"### Motivation"` headings under the same parent — the collision case that actually
occurs, e.g. a proposal repeating a template's section names under two different top-level
alternatives whose own path already differs — gets `"... > Motivation"` and `"... > Motivation
(2)"`; a document with no repeats pays nothing for the mechanism.

## Content paging (`get_language_doc`)

`limit` here is a **character budget per page**, not an item count — the one asymmetry against the
other two tools' paging, because content is measured in prose length and search/outline results are
measured in count. Default 8000, clamped to [1000, 50000].

The page starts at a line (1 for a whole-document fetch, the section's `StartLine` for a sectioned
one, or wherever the previous page's cursor left off) and accumulates whole lines until the running
character count would exceed the budget on the next line. It then checks `MarkdownAtomicBlocks`: if
the boundary line falls inside a fenced code block or a table, the page extends to that block's
`EndLine` regardless of budget, so a page never ends mid-word (lines are already atomic) and never
mid-table or mid-fence. The page always stops at the section's `EndLine` (or end of file for a
whole-document fetch) even if under budget.

The response carries `path`, the source's provenance, `section` (echoed verbatim, absent for a
whole-document fetch), `text` (the page's lines, newline-joined), `startLine`, `endLine` (both
1-based, inclusive, so a caller can orient a page against the outline), `isPartial`, and
`nextPageToken`. There is no size cap and no refusal, per the standing design: a section larger than
the budget pages instead of erroring.

## Item paging (`search_language_docs`, `get_language_doc_outline`)

Both page by item count, reusing `ApiDocsQueryService`'s exact cursor scheme: a base64url-encoded
`PageCursor(Version, Kind, Scope, Offset, Revisions)`, rejected if `Kind` or `Scope` don't match the
current request or `Revisions` don't match the currently-searched sources' `repo@ref@commit`
strings. `Kind` is `"lang-search"` or `"lang-outline"` — new values, distinct from the API tools' —
so a cursor from one tool is never mistakenly honored by another.

`Scope` generalizes from a single string to a JSON-encoded tuple of whatever must match for the
cursor to still make sense: `(query, regex, source)` for search, `(source, path)` for outline.

- `search_language_docs`: `limit` 1–100, default 20, matching `search_api`'s convention exactly.
  Every configured markdown-capable source is searched when `source` is omitted (today: `csharplang`
  and `vblang` — the supported set is configuration, not code, matching the existing design note).
  Hits across sources are ordered by path, then line, then source repo, all ordinal, before paging —
  the same flattened-single-sequence approach `ApiDocsQueryService.LookupAsync` already uses for
  `(type, member)` pairs, for the same reason: one pagination state, not one per source.
- `get_language_doc_outline`: `limit` 1–500, default 100. A heading is a handful of bytes, so even
  the largest spec or meeting-notes document tops out at a few hundred entries — the ceiling is
  headroom, not an expectation that real documents need a second page.

## Path addressing

`path` is forward-slash-separated and relative to the named source's synced root — exactly the
string a `search_language_docs` hit returns in its own `path` field, e.g.
`"proposals/csharp-11.0/generic-math.md"`. `source` is mandatory alongside it in `get_language_doc`
and `get_language_doc_outline` because the same relative path can exist in both sources (both keep
a `spec/` and a `proposals/` folder) and nothing else disambiguates it.

## Error taxonomy

Extends the existing `source_not_synced` / `invalid_cursor` / `invalid_request` / `git_timeout`
codes (identical meaning, identical exception mapping in the tool layer) with two new ones. A
`source` that is neither `"csharplang"` nor `"vblang"` is **not** a new code: verified against
`ApiDocsTool`, an unrecognized `source` there is an `ArgumentException` caught by the existing
generic mapping and reported as `invalid_request`, not `source_invalid` — that code is reserved for
an `InvalidDataException`, a structurally broken *already-synced* source (ApiDocs' example: its
docs-root subfolder is missing). Nothing analogous is reachable here, because a language-doc
source's root is the sparse checkout itself with no subfolder indirection, and
`SourceSynchronizer`'s integrity check already refuses to consider a sync complete unless every
sparse path exists. So an unrecognized `source` for these tools reuses `invalid_request` too, and
`source_invalid` is not part of this surface.

- `path_not_found` — `path` doesn't resolve to a synced markdown file: it escapes the source's
  synced root (the same traversal guard as `ApiDocsQueryService.ResolveNamespaceDirectory`), or no
  file exists there.
- `section_not_found` — `section` doesn't match any heading `Path` currently in the document's
  outline. The message names the remedy: "Call `get_language_doc_outline` to see valid section
  paths for this document." This is the expected way a stale section path (from a search hit read
  before a re-sync moved the heading) surfaces, rather than silently returning the wrong content.
- `invalid_regex` — `regex: true` and the pattern doesn't construct under
  `RegexOptions.NonBacktracking`. The message is the framework's own exception text, which already
  names the unsupported construct.

## Testing

`DotNetKnowledge.Markdown`, new project, unit tests over literal markdown strings:

- heading extraction for both ATX and setext forms, including a heading inside a fenced code block
  correctly ignored;
- path disambiguation — two same-path siblings get `Path` and `Path (2)`; no false positive across
  documents (irrelevant here, since the library operates one document at a time) or across
  non-colliding paths;
- paging never ends inside a fenced code block or a table, verified by a fixture string engineered
  so the naive character cutoff would land inside one;
- literal search matches substrings case-sensitively; regex search matches patterns; a pattern using
  a backreference throws before any match is attempted.

`DotNetKnowledge.Mcp.Tests`, new `Features/LanguageDocs/` folder, following the existing
`ApiDocsQueryServiceTests`/`ApiDocsToolTests` split:

- query-service tests run against the already-synced `vblang` fixture in this machine's cache (the
  smaller of the two configured markdown sources);
- `source_not_synced` when the source has never been synced;
- `path_not_found` for a path outside the synced tree and for one that escapes it (`../../etc`);
- `section_not_found` for a section string that doesn't match the current outline, naming the
  remedy;
- `invalid_regex` for a backreference pattern;
- a cursor issued against one source revision is rejected after a re-synchronization, for both
  paged tools;
- the provenance envelope is present on every response shape, including error responses that carry
  `searchedSources`.

## Documentation

Implementation-time updates, not part of this spec:

- `docs/design/mcp-tool-surface.md` — add the numeric limit defaults/bounds for all three tools
  (matching how `lookup_api`'s already appear there) and give `get_language_doc_outline` its
  `limit?`/`cursor?` parameters, which the current surface text omits.
- `README.md` — the status section drops "Language design-document queries... remain future work"
  once the tools pass under a client, matching how the API-tools line was updated in the prior
  defect-remediation change.
- `docs/decisions.md` gains entries for: the markdown parsing/paging logic living in its own
  library project rather than inside the server (rejected: keeping it in `Features/LanguageDocs/`
  directly, which would make it unreusable and untestable without the server's other dependencies);
  character-budget paging for document/section content, snapped to atomic block boundaries
  (rejected: line-count paging, which bounds response size unpredictably since prose line length
  varies enormously between a spec paragraph and a grammar production); and collision-suffix-only
  disambiguation for section paths (rejected: unconditional sibling numbering, which makes every
  path more verbose for a collision that is rare in practice).
