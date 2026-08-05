# `lookup_api` reports `not_found` for a cursor landing exactly at the end

`ApiDocsQueryService.LookupAsync` rejects a cursor only when its offset is strictly past the result
set:

```csharp
if (offset > pairs.Length)
    throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));
```

At `offset == pairs.Length` the page is legitimately empty. `ApiDocsTool.LookupApi` then sees
`Matches.Count == 0` and answers `not_found` — "API symbol 'X' was not found in the selected
synchronized source(s). Call search_api with a type-name fragment to find candidates." — for a symbol
that plainly exists and whose earlier pages the caller has already read.

## Why it matters

The answer is not merely unhelpful, it is false, and its remedy sends the caller to a tool that
cannot resolve the situation. `search_api` will confirm the type exists, which contradicts the error
the caller was just given.

`search_api` does not share the defect: at the same boundary it returns an empty page with
`isPartial` false and no error, which is the correct shape.

## Evidence

Not reachable through the documented flow. `nextPageToken` is issued only when `isPartial` is true —
that is, only when at least one more item exists — and a cursor is bound to the symbol and the source
revisions, so it cannot survive a re-synchronization that shrinks the result set. Reaching the
boundary requires hand-constructing a cursor at exactly `pairs.Length`.

## Suggested fix

Distinguish "no matches" from "a valid page that happens to be empty" before the tool maps the result
to an error. The outcome enum `ApiLookupResult` already carries is the natural place: an empty page at
a valid offset is `Found` with zero items, not `TypeNotFound`.

Mirroring `search_api` — an empty page, `isPartial` false, no error — makes the two tools consistent
at the boundary, which is worth more than either behavior alone.
