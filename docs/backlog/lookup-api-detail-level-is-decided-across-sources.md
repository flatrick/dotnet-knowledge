# `lookup_api`'s detail level is decided across all searched sources

`ApiDocsQueryService.LookupAsync` picks between the signatures-only and full-documentation tiers
with a single expression covering every source it read:

```csharp
var signaturesOnly = reads.Any(read => read.Matches.Count > 0 && !read.SymbolNamedAMember);
```

If one source resolves the symbol as a *type* and another resolves the same string as
`Type.Member`, and both produce matches, the whole response collapses to signatures only — including
the matches from the source that resolved a member and for which full documentation was the correct
answer.

## Why it matters

The two tiers exist so that naming a member is how a caller asks for its documentation. A caller who
names a member and receives a bare signature has no way to tell that the tier was decided by a
different source's interpretation of the same string, because nothing in the response records which
reading won.

The failure is quiet in the safe direction — the response is too small, never wrong — which is why it
is deferred rather than fixed.

## Evidence

Not currently reachable. The two configured sources have disjoint namespace trees: `dotnet-api-docs`
holds `xml/System.*` and `roslyn-api-docs` holds `dotnet/xml/Microsoft.CodeAnalysis.*`, and symbol
resolution is directory-based, so no symbol string resolves as a type in one and as a member in the
other. A third source overlapping either namespace would make it reachable.

No test covers the disagreement in either direction.

## Suggested fix

Decide the tier per source rather than per request, so each source's matches are rendered at the tier
its own resolution selected. `LookupRead` already carries `SymbolNamedAMember` per source; the
information needed is present and only the aggregation discards it.

Whichever way it is resolved, the response should state which reading produced it, so a caller can
tell a signatures-only answer from a signatures-only *decision*.
