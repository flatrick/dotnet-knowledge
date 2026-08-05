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
