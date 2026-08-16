# `lookup_api` orders overloads by signature ordinal

`ApiDocsQueryService` flattens a type's members with
`.OrderBy(member => member.Name, StringComparer.Ordinal).ThenBy(member => member.Signature, StringComparer.Ordinal)`.
Overloads share a name, so the whole ordering falls to the second key: the rendered C# signature
compared ordinally.

An ordinal comparison sorts by code point, and a signature names its parameter types the way the
documentation spells them — fully-qualified type names keep their uppercase namespace, C# keywords
stay lowercase. `System.Text.Rune` therefore sorts ahead of `char`, which sorts ahead of `string`,
for no reason connected to what a caller wants.

## Why it matters

`limit` pages over this sequence, so the ordering decides what lands on page one, and an agent that
reads one page and stops sees whatever the code points chose.

The interaction with upstream documentation gaps is what makes it bite. An overload documented only
as ECMA `"To be added."` contributes no `summary`, no parameter descriptions and no `returns` — the
server correctly omits the placeholder rather than surfacing it. When such an overload also sorts
first, page one is bare signatures, and the documented overloads the caller was asking about are
past the cursor.

## Evidence

`lookup_api(symbol: "System.String.Contains", source: "dotnet-api-docs")` returns six overloads in
this order, against `dotnet/dotnet-api-docs` at `a81557d3`:

1. `Contains (System.Text.Rune value)` — no prose
2. `Contains (System.Text.Rune value, StringComparison comparisonType)` — no prose
3. `Contains (char value)`
4. `Contains (char value, StringComparison comparisonType)`
5. `Contains (string value)`
6. `Contains (string value, StringComparison comparisonType)`

`S` is `0x53`, `c` is `0x63`, `s` is `0x73`, which is the order above and is not document order —
`Contains (char value)` is the first `Contains` block in `xml/System/String.xml`.

Both `Rune` overloads carry `<summary>To be added.</summary>` upstream. So
`lookup_api(symbol: "System.String.Contains", limit: 2)` answers with two signatures, zero
documentation and `isPartial: true` — every field correct, and the least useful two of six.

## Suggested fix

Rank overloads before falling through to the signature, and keep the signature as the final
tiebreak so the sequence stays total.

Two signals are available without reading the query, which matters because `lookup_api` takes a
symbol rather than a phrase and has nothing to score relevance against:

- **Documented before undocumented.** A member with a `summary` outranks one without. This is the
  signal that fixes the observed case, and it is the one an agent actually needs.
- **Arity, then parameter-type simplicity.** Fewer parameters first, and among equal arity prefer
  keyword-spelled primitives over fully-qualified types — an approximation of "the common overload",
  and the thing ordinal comparison currently inverts.

Whatever the keys, the ordering must stay **deterministic and independent of the request**: paging
runs over one flat member sequence across every match and cursors bind offsets into it, so an
ordering that varied between calls would make a cursor point somewhere else. That constraint rules
out anything query-dependent or time-dependent, not the ranking itself.

The same `.OrderBy(...).ThenBy(...)` shape appears for search hits and reference hits. Only the
member sequence has the overload-collision problem, because only there does the first key tie for
every row; leave the others alone.

## Probably best done together with the dedup item

[`search_api_text`'s dedup key never collapses overloads](search-api-text-dedup-key-never-collapses-overloads.md)
is the same shape of problem: both arise where a set of members shares one name, and both are
currently resolved by a key that ties.

They are worth scheduling together rather than separately:

- Both live in `ApiDocsQueryService`, a couple of hundred lines apart — the member ordering at
  `:103-110`, the text dedup at `:300-313`.
- Both need the same judgement about what distinguishes one overload from another in a payload, and
  the answers should agree. `lookup_api` already returns a `signature` per member; `search_api_text`
  returns none. Deciding that once is cheaper than deciding it twice and reconciling later.
- One fixture serves both: an overload set whose members carry identical prose, with some documented
  and some not. That single shape exercises the ranking change and the collapse change at once.

Neither depends on the other, so either can ship alone. The saving is in doing the thinking once.
