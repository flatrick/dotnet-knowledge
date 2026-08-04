# A non-generic type shadows its generic namesake

`ApiDocsQueryService.FindTypeFilesInNamespace` looks for an exact `<simpleName>.xml` first and
returns it alone when it exists; only when it is absent does it fall back to the
`` <simpleName>`*.xml `` glob that finds generic arities. When a namespace holds both — a
non-generic type and a generic type of the same name — the generic one is unreachable by its plain
name.

## Why it matters

The shadowed type is usually the one the caller wants. `Microsoft.CodeAnalysis` holds both
`SyntaxList.xml`, a static helper with a single `Create` method, and `` SyntaxList`1.xml ``, the
`SyntaxList<TNode>` collection that appears throughout the Roslyn API surface.

The response is not visibly incomplete. It is a successful lookup naming a real type with real
members, so nothing tells the caller that a second, larger type of the same name was passed over.
Qualifying the name does not help, because the shadowing happens after the namespace is resolved.

## Evidence

Against `roslyn-api-docs` at the pinned commit:

| Symbol | Matches | Members |
|---|---|---|
| `SyntaxList` | `Microsoft.CodeAnalysis.SyntaxList` | 1 |
| `Microsoft.CodeAnalysis.SyntaxList` | `Microsoft.CodeAnalysis.SyntaxList` | 1 |
| `` SyntaxList`1 `` | `Microsoft.CodeAnalysis.SyntaxList<TNode>` | 36 |
| `` Microsoft.CodeAnalysis.SyntaxList`1 `` | `Microsoft.CodeAnalysis.SyntaxList<TNode>` | 36 |

The escape hatch works: a caller who already knows the ECMA file-stem spelling gets the right type,
and `search_api` returns exactly that spelling, so a search result can be fed back into `lookup_api`
verbatim. That round-trip is what makes this a defect worth fixing rather than a blocker — the type
is reachable, but only by a caller who searched first or who knows the backtick convention.

Where no non-generic sibling exists the glob works as intended: `IImmutableSet` resolves to
`System.Collections.Immutable.IImmutableSet<T>` with 15 members, and `List` returns
`System.Collections.Generic.List<T>` among its matches.

## Suggested fix

Return the exact match *and* the arity glob rather than choosing between them, ordered so the exact
non-generic match comes first. `lookup_api` already returns several matches for one name — `List`
yields three — so multiple matches need no new response shape, and the caller can see that both
types exist.
