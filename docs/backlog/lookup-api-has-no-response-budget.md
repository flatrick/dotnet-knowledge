# lookup_api has no response budget

`lookup_api` with a bare type name returns every member of that type, each with its summary,
parameter documentation, returns text and full `remarks`, serialized with `WriteIndented = true`.
Nothing bounds the result: there is no page, no limit, and no way to ask for signatures without
documentation bodies.

## Why it matters

`docs/design/mcp-tool-surface.md` reasons carefully about exactly this cost for the search tools —
"an agent pays for every token it receives", which is why `search_api` returns names only and
`search_language_docs` is specified to return `path:line` hits. `lookup_api` is the tool that surface
is designed to funnel callers *into*, and it is the one with no budget at all.

A single call can consume a large fraction of a context window. That makes the cheap-search design
self-defeating: the agent narrows for almost nothing, then spends more on one lookup than an
unbounded search would have cost.

The types most likely to be looked up are the worst affected, because member count and documentation
volume both scale with how commonly a type is used.

## Evidence

Wire bytes returned by `ApiDocsTool.LookupApi` against the pinned sources:

| Symbol | Members | Wire bytes |
|---|---|---|
| `String` | 235 | 427 817 |
| `List` | 82 across 3 types | 183 005 |
| `Workspace` | 167 across 2 types | 81 955 |
| `Console.WriteLine` | 20 | 40 181 |
| `SymbolFinder` | 31 | 32 376 |
| `SymbolFinder.FindCallersAsync` | 2 | 2 168 |

`String` alone is roughly 107 000 tokens at four bytes per token.

Restricting to a member is the only lever that helps, and it is not always available: it collapses
`SymbolFinder` from 32 376 bytes to 2 168, but a caller who does not yet know which member they want
has no way to ask for the list cheaply.

`List` also shows the multiplier: an unqualified name matches `System.Collections.Generic.List<T>`,
its nested `Enumerator`, and the unrelated `System.Windows.Documents.List`, and every match is
returned in full.

## Suggested fix

Give `lookup_api` the same explicit-pagination treatment `search_api` already has, and add a
detail level so a caller can ask for signatures without documentation bodies. A signatures-only
response for a whole type, with `lookup_api(symbol: "Type.Member")` for the bodies, matches the
two-step narrowing the design already argues for.

`remarks` deserves its own decision. In ECMA XML it is frequently a long prose block, and it is the
single largest contributor to these figures; excluding it from a whole-type response, and returning
it only for a specifically requested member, would cut the cost sharply without losing anything a
caller cannot ask for.
