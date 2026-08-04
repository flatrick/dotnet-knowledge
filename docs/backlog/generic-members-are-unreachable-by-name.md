# Generic members are unreachable by name

`lookup_api("Type.Member")` matches a member with `string.Equals(..., StringComparison.Ordinal)`
against the `MemberName` attribute in the ECMA XML. For a generic method that attribute carries the
type-parameter list, so the name an agent would ask for never matches:

```
MemberName="Select&lt;TSource,TResult&gt;"
MemberName="FirstAncestorOrSelf&lt;TNode&gt;"
```

`ApiDocsQueryService.ReadType` filters members on that attribute, and `ReadLookupSource` then drops
any type whose filtered member list is empty. The tool reports `not_found`.

## Why it matters

The affected set is large and central. It includes every LINQ operator — `Select`, `Where`,
`OrderBy`, `OfType`, `Cast` — and Roslyn's generic navigation helpers such as
`SyntaxNode.FirstAncestorOrSelf`. These are among the most likely things an agent asks about.

The failure is a clean `not_found`, indistinguishable from a symbol that genuinely does not exist,
so a caller has no signal that the member is present and merely unmatched.

The remedy the error names cannot resolve it:

> Call search_api with a type-name fragment to find candidates.

`search_api` enumerates type files and returns type names only — `ReadSearchSource` never opens a
document or reads a `Member` element. No `search_api` call can surface a member, so an agent that
follows the instruction searches, finds the type it already had, and is no closer.

## Evidence

Against the pinned sources, queried through `ApiDocsTool`:

| Symbol | Result |
|---|---|
| `Enumerable.Select` | `not_found` — 0 matches |
| `SyntaxNode.FirstAncestorOrSelf` | `not_found` — 0 matches |
| `SymbolFinder.FindCallersAsync` | 2 members — non-generic, so it matches |
| `Console.WriteLine` | 20 members — non-generic |

`System.Linq.Enumerable` holds `Select&lt;TSource,TResult&gt;`, and
`Microsoft.CodeAnalysis.SyntaxNode` holds both `FirstAncestorOrSelf&lt;TNode&gt;` and
`FirstAncestorOrSelf&lt;TNode,TArg&gt;`.

No test covers this. The three tests over the query surface use synthetic fixtures
(`System.AlphaWidget`, `System.BetaWidget`) whose XML carries no generic member.

## Suggested fix

Match the member name against the attribute value truncated at its first `<`, so `Select` matches
`Select<TSource,TResult>`, while still accepting the fully-specified form when a caller supplies it.
Returning every arity for a requested name is the right default: overloads that differ only in type
parameters are what a caller asking for `Select` wants to see.

Separately, correct the `not_found` message. Directing the caller to `search_api` is sound when the
*type* was not found and wrong when the type resolved and the member did not — those two cases are
already distinguishable at the point the message is produced.
