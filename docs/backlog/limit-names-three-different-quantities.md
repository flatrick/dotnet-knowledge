# `limit` names three different quantities across one surface

Seven of the nine tools take a parameter spelled `limit`. It does not mean the same thing in each,
and the accepted range differs three ways:

| Tools | Range | What it counts |
|---|---|---|
| `search_docs`, `lookup_api`, `search_api`, `search_api_text`, `find_api_references` | 1–100 | result items |
| `get_doc_outline` | 1–500 | heading entries |
| `get_doc` | 1000–50000 | characters of text |

## Why it matters

An agent that learns `limit` from one tool carries the wrong model to the next. `limit: 100` is
valid on six tools and an error on `get_doc`; `limit: 1` is valid on six and an error on `get_doc`;
`limit: 200` is valid only on `get_doc_outline`.

The underlying quantities really are different — a character budget is not an item count, and
`get_doc`'s floor of 1000 exists because a budget below that cannot hold an atomic block such as a
fenced code fence or a table row. That is sound. What is not sound is spelling three quantities with
one name and leaving the caller to discover the difference by being rejected.

This is a discoverability cost, not a correctness one. The errors are clean and state the right
range, so an agent recovers on the second call — it just pays a round trip that the parameter name
caused.

## Evidence

- `Features/Docs/DocsQueryService.cs:38-39` — outline, `1 or > 500`.
- `Features/Docs/DocsQueryService.cs:74-75` — document search, `1 or > 100`.
- `Features/Docs/DocsQueryService.cs:174-175` — document fetch, `1000 or > 50000`.
- `Features/ApiDocs/ApiDocsQueryService.cs:62, 197, 283, 354` — all four API tools, `1 or > 100`.
- Observed against the installed server at `53882bd`: `get_doc(limit: 100)` returns
  `invalid_request` "limit must be between 1000 and 50000." while `search_docs(limit: 100)`
  succeeds.
- No tool description states its range. `get_doc_outline`'s says "Paginated like the other tools"
  (`Features/Docs/DocsTool.cs:162`) while accepting five times their ceiling, so the one description
  that addresses paging at all points the wrong way.

## Suggested fix

Prefer naming over unifying. The ranges encode real constraints and collapsing them would either
raise `get_doc`'s floor onto tools that do not need it or lower it below what an atomic block
requires.

Options, cheapest first:

- **Say it in the tool description.** `get_doc` and `get_doc_outline` already carry long
  descriptions; neither states its range. One clause each is nearly free and removes most of the
  cost.
- **Rename the character budget.** `maxCharacters` on `get_doc` would make the difference visible in
  the schema rather than in an error, at the price of a breaking change to a parameter name.

Whichever is chosen, the three ranges should be stated in
[`docs/design/mcp-tool-surface.md`](../design/mcp-tool-surface.md), which currently describes paging
as one mechanism.
