# `search_api_text`'s dedup key never collapses overloads

`ApiDocsQueryService.SearchTextAsync` deduplicates hits on
`(DeclarationId, Element, NormalizedText)`. Its comment states the intent:

> Overloads each carry their own Docs under one MemberName, so identical prose on `Create(a)` and
> `Create(a, b)` would otherwise arrive as two hits a caller cannot tell apart.

`DeclarationId` is the ECMA documentation ID, which encodes the parameter list, so every overload
has a different one. The key therefore always differs and `DistinctBy` never collapses an overload
set. The dedup is inert for the one case it was written for.

## Why it matters

The rows are indistinguishable, because the payload carries no overload identity either: a hit's
`symbol` is the member name, the same string for every overload. So the caller receives N rows that
agree in `symbol`, `element` and `text`, with nothing to tell them apart and nothing gained by
keeping them.

The compounding cost is the per-symbol cap. `ApiTextRanking.CollapsePerSymbol` keeps at most
`TextHitsPerSymbol` (2) hits per symbol, so both slots are spent on the same sentence and anything
genuinely different from that symbol — another overload's differing summary, a `remarks` match — is
pushed behind `moreFromSymbol`. A duplicate does not merely waste a row; it evicts distinct prose
that would otherwise have been returned.

## Evidence

- `Features/ApiDocs/ApiDocsQueryService.cs:300-310` — the key, and the comment describing the
  behavior it does not achieve.
- `Features/ApiDocs/ApiDocsQueryService.cs:311-313` — `CollapsePerSymbol` runs after the dedup, so
  undeduplicated rows consume the cap.
- The pinned `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.3.0 corpus holds four `Create` overloads
  with four distinct IDs and byte-identical summaries:

  ```
  M:...MSBuildWorkspace.Create
  M:...MSBuildWorkspace.Create(Microsoft.CodeAnalysis.Host.HostServices)
  M:...MSBuildWorkspace.Create(System.Collections.Generic.IDictionary{System.String,System.String})
  M:...MSBuildWorkspace.Create(System.Collections.Generic.IDictionary{System.String,System.String},Microsoft.CodeAnalysis.Host.HostServices)
  ```

  all documented "Create a new instance of a workspace that can be populated by opening solution and
  project files."

- Observed against the installed server at `53882bd`:
  `search_api_text(query: "solution and project files", source: "roslyn-api-docs")` returns two rows
  for `...MSBuildWorkspace.Create` with identical `element` and `text`, and `moreFromSymbol: 2` on
  the second.
- Both backends key hits per declaration — `PackageApiDocsBackend.SearchText` passes
  `member.EcmaId`, and `RepositoryApiDocsBackend` carries canonical ECMA IDs it actively guards
  against collision — so this is not specific to the package half. The dedup runs over the merged
  reads, so one fix covers both.

## Suggested fix

Deduplicate on what the caller can see. If two rows agree in every returned field they are
duplicates by definition, so the key should be the payload's identity —
`(Symbol, Element, NormalizedText)` — rather than the corpus's.

Report the collapse rather than performing it silently: carry a count of how many declarations
shared the prose, so "all four overloads are documented identically" stays visible. That is the same
obligation `isTruncated` and `skippedDeclarations` serve elsewhere.

```
{ "symbol": "...MSBuildWorkspace.Create", "element": "summary",
  "text": "Create a new instance of a workspace...", "declarations": 4 }
```

Two things to get right while making the change:

- **Dropping `DeclarationId` must not regress the cross-source case.** Today the key also collapses
  one declaration reported by both backends. It still will: same symbol, same element, same text.
  Prose that differs between the two halves survives as separate rows, which is correct — but the
  collapsed row must then name a deterministic source. Repository-first, matching the merge
  precedence the rest of `ApiDocsQueryService` already applies.
- **Keep the dedup where it is**, ahead of `Order` and `CollapsePerSymbol`. Collapsing after the cap
  would let duplicates evict distinct prose first and only then merge, which is the current defect
  with an extra step.

Carrying the full list of contributing signatures instead of a count was considered and deferred: a
summary shared across a large overload set would need its own cap and its own truncation flag, and
the count answers the question the caller actually has. Revisit if the count proves too thin.

## Probably best done together with the ordering item

[`lookup_api` orders overloads by signature ordinal](lookup-api-orders-overloads-by-signature-ordinal.md)
is the same shape of problem: both arise where a set of members shares one name, and both are
currently resolved by a key that ties.

They are worth scheduling together rather than separately:

- Both live in `ApiDocsQueryService`, a couple of hundred lines apart — the member ordering at
  `:103-110`, the text dedup at `:300-313`.
- Both need the same judgement about what distinguishes one overload from another in a payload, and
  the answers should agree. `lookup_api` already returns a `signature` per member; `search_api_text`
  returns none. Deciding that once is cheaper than deciding it twice and reconciling later.
- One fixture serves both: an overload set whose members carry identical prose, with some documented
  and some not. That single shape exercises the collapse change and the ranking change at once.

Neither depends on the other, so either can ship alone. The saving is in doing the thinking once.
