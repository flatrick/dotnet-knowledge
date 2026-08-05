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
