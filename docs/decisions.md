# Decisions

Standing decisions and the alternatives they rejected. A decision belongs here when reopening it
would cost real work — the value is the rejected options, not the chosen one.

**Append-only.** Never edit or delete an entry. A decision that no longer holds is replaced by a new
entry naming the one it supersedes, so the record of having already asked survives. Newest first, so
reading downward reaches current truth before it reaches anything corrected. This preamble is not an
entry and may be revised.

This file is exempt from convention 3 in [`AGENTS.md`](../AGENTS.md) — a standing decision is a live
constraint, not narration of history.

**A decision is not a rule.** A rule is an obligation that always applies and lives in
[`AGENTS.md`](../AGENTS.md) or [`CLAUDE.md`](../CLAUDE.md). A decision records what was chosen over
what, and why. A decision may impose a rule, and then both exist and the rule cites it.
A hazard rather than a choice is a [gotcha](gotchas.md).

**Write an entry when** a spec, review, or experiment chose between real alternatives.

**Four lines per entry.** If it needs more it is a spec under
[`docs/superpowers/specs/`](superpowers/specs/) or a file in [`docs/backlog/`](backlog/README.md),
and the entry links there.

---

### 2026-08-09 · A caller-input encoding miss gets one normalized retry, not a global filter

`get_doc`'s `section` and `path`, `get_doc_outline`'s `path`, and `search_docs`'s non-regex `query`,
retry once against an HTML-entity/typography-decoded form of the caller's input, and only after the
literal value has already failed to match — never before. A response produced this way reports the
resolved value, not the caller's spelling, and carries `normalizationNote` naming the substitution.
When the retry fails too, the error names what the caller actually sent, never the internally-decoded
guess — including when the guess fails a different way than the caller's own input would have (a
decoded NUL character rejected by `Path.GetFullPath`, for instance). Rejected: normalizing every
string parameter unconditionally at the tool boundary, which would make a legitimately-authored
`&gt;` in real heading text unreachable and would leave nothing to compare once the raw form is gone,
so nothing to report. See
[`docs/superpowers/specs/2026-08-09-caller-input-normalization-design.md`](superpowers/specs/2026-08-09-caller-input-normalization-design.md).

---

### 2026-08-08 · Front matter is metadata, excluded from both search and fetch

`MarkdownFrontMatter.BodyStartLine` is the one rule; `MarkdownLineSearch` skips lines before it and
`GetDocAsync` starts a whole-document range there. Supersedes the "frontmatter stays searchable"
property of
[`docs/superpowers/specs/2026-08-08-nuget-docs-source-design.md`](superpowers/specs/2026-08-08-nuget-docs-source-design.md).
Rejected: keeping it searchable, whose stated reason was that suppressing it would manufacture a
silent absence. Measured over `nuget-docs` at the pin, front-matter keys are 451 of 521 lines
matching `description` (87%), 451 of 482 matching `title` (94%) and 872 of 1105 matching `author`
(79%; the count is larger than the other rows' 451 because `ms.author:` also contains the word) —
against a page capped at 20 — and once `get_doc` starts after the front matter those hits carry an
empty section path and name lines no call returns, which is a worse failure than the absence.
Also rejected: excluding it from `get_doc` only, which leaves search and fetch disagreeing about
what exists; and returning the parsed keys as a structured field, which no client wants.
Spec: [`docs/superpowers/specs/2026-08-08-front-matter-is-not-content-design.md`](superpowers/specs/2026-08-08-front-matter-is-not-content-design.md).

---

### 2026-08-08 · The document tools drop "language" from their names

`search_language_docs`/`get_language_doc`/`get_language_doc_outline` become
`search_docs`/`get_doc`/`get_doc_outline`, and `Features/LanguageDocs/` becomes `Features/Docs/`
with its types renamed to match; error codes, JSON field names and cursor `kind` strings are
unchanged, so no previously issued cursor is rejected.
Rejected: keeping the names and broadening only the descriptions, which leaves a tool an agent
cannot discover from its name — the capability then exists only for a caller who reads the
description. Also rejected: a parallel `search_nuget_docs` family, which adds three tool definitions
to every agent's context for no new capability and makes each future markdown source repeat the
pattern; and deprecated aliases, which put six definitions in context to describe three capabilities
with no forcing function to ever remove them.

---

### 2026-08-08 · NuGet guidance ranks below language proposals and above release notes

`DocRanking.DocumentTypeRank` goes to four tiers: proposals and spec 0, current NuGet guidance 1,
everything else 2, meeting notes and NuGet release-notes/archive 3.
Rejected: a flat tier for NuGet. Equal ranks fall through to the path tiebreak, where `docs/` sorts
ahead of `proposals/`, so an unfiltered language query would have been answered by packaging
documents. Rejected: deferring the ordering to the backlog — of 1170 NuGet lines matching
`restore`, 479 are release notes or archive, so against the default limit of 20 the source would
have shipped measurably worse than it needed to be, and an agent holding twenty historical hits has
no signal that better documents sit below the cut. The reverse direction costs nothing:
`PackageReference`, `package restore`, `nuspec` and `central package management` each return 0 hits
across csharplang, vblang and roslyn-wiki combined. Spec:
[`docs/superpowers/specs/2026-08-08-nuget-docs-source-design.md`](superpowers/specs/2026-08-08-nuget-docs-source-design.md).

---

### 2026-08-08 · The language-feature example corpus moves to its own repository

`examples/language-features/`, `Corpus.slnx`, `tests/DotNetKnowledge.Corpus.Tests/`, the
floor/placement/namespace verification scripts, and the corpus-only docs moved to
[flatrick/dotnet-code-examples](https://github.com/flatrick/dotnet-code-examples) with no shared
git history, leaving this repository as the MCP server only. Rejected: keeping the corpus bundled
and adding it to `sources.json` as a self-referential fetch, which would have made the server clone
its own repository for no benefit. The corpus has no dependency on MCP tooling — it exists to be
read directly and to give tools like Roslyn analyzers and other .NET tooling a realistic
multi-project, multi-TFM target — and outgrew being scoped to one server's bundled content the
moment a second, unrelated consumer needed it.

### 2026-08-06 · A query for `Foo` excludes `FooAttribute`'s applications and names the sibling

ECMA XML spells an attribute application in C# short form, so 78 of 617 attribute types collide with
a de-suffixed sibling in the same namespace. Unioning the two readings was rejected: it inflates the
`attribute` total with hits belonging to a different type, and any caller filtering on `kind` alone
gets a wrong count. Excluding them silently was rejected as a plausible absence. The response
therefore carries a `note` naming the sibling, its application count, and the call that reaches it —
the shape `lookup_api`'s `member_not_found` envelope already uses.

### 2026-08-06 · Whole-corpus scans stay uncached

`search_api_text` and `find_api_references` re-read every XML file in every selected source on every
call, measured at 0.5-0.7 s for the parallel prefilter and 0.7-3.6 s end to end through the host —
inside what a caller waits for, with no observed need for anything faster. Rejected: an index, which
puts a build step between a sync and a correct answer and answers from a stale one with plausible
absences; and memoizing the surviving file list per commit, which is cheap but buys nothing yet.
Revisit only on a measured complaint, not on a suspicion.

### 2026-08-05 · Cached clones get `feature.manyFiles`, never `core.fsmonitor`

Every staging repository is configured with `feature.manyFiles` and `core.untrackedCache` before its
checkout; these caches index tens of thousands of paths, and both settings are observed to improve
working with this repository. Rejected: `core.fsmonitor`, which would help more and starts a
`git fsmonitor--daemon` per repository that this server neither spawns nor supervises and that
outlives it — the inherited-handle hazard in [`gotchas.md`](gotchas.md); and `core.commitGraph`,
since the sync never walks history.

### 2026-08-05 · A whole-tree read gets its own tier, and a failed sync keeps its download

`git status --untracked-files=all` moved to `GitCommandKind.Walk` (2 min): its cost scales with the
checkout, so the 10 s metadata ceiling killed a valid 13,485-file sync. Staging is now retained on
failure and resumed, rather than discarding 773 MB. Rejected: raising `Quick`, which leaves
`rev-parse` unbounded for no reason; and dropping `--untracked-files=all`, which is the check that
catches a half-written sparse checkout. Supersedes the 2026-08-05 tier-naming entry only in count.

### 2026-08-05 · The markdown-searchable source set is a `sources.json` field, not a hardcoded list

`SourceDefinition.Markdown` (JSON `"markdown"`) marks which sources
`search_language_docs`/`get_language_doc`/`get_language_doc_outline` can reach; `LanguageDocsQueryService`
reads it instead of intersecting a hardcoded `["csharplang", "vblang"]` array against the catalog.
This also unlocked `roslyn-wiki`, already configured in `sources.json` as a pure-markdown source with
no code path that could reach it. Rejected: the hardcoded allowlist, which the design doc already
claimed was "configuration, not code" while the code said otherwise.

### 2026-08-05 · Markdown parsing lives in its own library, not inside the server

`DotNetKnowledge.Markdown` holds heading extraction, atomic-block detection, character-budget
paging, and line search, with no dependency on the MCP server, `SourceCache`, or JSON. Rejected:
building this directly in `Features/LanguageDocs/`, which would make it unreusable and untestable
without the server's other dependencies.

### 2026-08-05 · `get_language_doc` pages by a character budget, not a line count

A budget bounds response size predictably regardless of how prose-heavy or grammar-production-heavy
a section is; it snaps to the nearest line boundary and never splits a fenced code block or a
table. Rejected: line-count paging, whose response size varies enormously between a one-sentence
paragraph and a wide grammar production.

### 2026-08-05 · A section-path collision gets a suffix only when it actually collides

Two headings with the exact same full ancestor-chain text (a rare but real case, e.g. a repeated
template section) get `Path` and `Path (2)`; every non-colliding path is untouched. Rejected:
unconditionally numbering every heading by sibling position, which makes the overwhelming majority
of paths — the ones that never collide — more verbose for no reason.

### 2026-08-05 · Language-doc retrieval addresses heading sections, not line ranges

`search_language_docs` hits and outline entries carry a server-issued heading path, and
`get_language_doc` returns that complete section — complete by construction, where a line range
returns what the caller guessed. Rejected: line-range parameters, GitHub anchor slugs (lossy and
collision-prone), and paragraph-level IDs, which markdown gives no native identity to.

### 2026-08-05 · The source cache lives in the user data directory, not the cache directory

`SourceCache` resolves `LocalApplicationData` — `~/.local/share` on Linux, `~/Library/Application
Support` on macOS — so a synced pin survives cache cleaners; query tools treat an absent source as
"call `sync_source`", never as something to refetch silently. Rejected: the XDG cache directory
(`~/.cache`), whose contract is precisely that its contents may be cleared at any time.

### 2026-08-05 · CI is configured but disabled; local runs are the only verification

Actions is off for this repository, so no workflow executes and nothing on `main` carries evidence
that it passed. The configuration is kept current so enabling it is a settings change, not a project.
Rejected: running the workflow for visibility, which costs private-repo minutes to tell us what a
local run already does.

### 2026-08-05 · Git timeout tiers are named for duration, not for the network

`GitCommandKind.Quick`/`Bulk` name what a command is expected to cost, because `sparse-checkout set`
and `checkout --detach FETCH_HEAD` write an ~806 MB working tree while using no network at all.
Rejected: the spec's `local`/`network` split, which leaves both of those commands unclassified.

### 2026-08-05 · Decisions and gotchas are append-only ledgers

Both files record entries permanently; supersession is declared forward by the newer entry.
Rejected: the `docs/backlog/` lifecycle of deleting a file when it stops being true, which loses the
record that a question was already settled — and, for a wrong answer, loses why it looked right.

### 2026-08-05 · The MCP Tasks extension is not adopted

`sync_source` reports progress through MCP progress notifications instead.
Rejected: `McpTaskExecutionMode.Optional`, which degrades to synchronous against a client that does
not declare the extension, and `Required`, which refuses the call outright. This client returns
-32021. See [the design](superpowers/specs/2026-08-05-mcp-server-defects-design.md).

### 2026-08-05 · `lookup_api` detail is selected by the shape of the requested symbol

A bare type name returns signatures; `Type.Member` returns full documentation.
Rejected: an explicit `detail` parameter, whose wrong setting is the defect being fixed, and
pagination alone, which spreads the cost of a 427 KB response rather than removing it.

### 2026-08-05 · Probes are separate from the shipped server

Diagnostic MCP servers live in [`scripts/probes/`](../scripts/probes/README.md) and are referenced by
nothing in `src/`. This lets them depend on experimental packages the production server will not
take, and keeps a harness that carries the fault under investigation away from the code being
investigated.
