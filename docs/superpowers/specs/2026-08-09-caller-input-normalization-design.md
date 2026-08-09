# Caller-input normalization fallback for exact-match query parameters

## Purpose

Several tools compare a caller-supplied string against server-known content by exact match (`section`, `symbol`) or substring match (`query`, `pattern`), and report not-found or zero hits when it doesn't match.
A subclass of those misses is not a real absence: the caller's string differs from the correct one only by an encoding artifact — an HTML entity (`&gt;` for `>`), a curly quote where the source has a straight one, a non-breaking space where it has a regular one — typically introduced when the value was copied out of rendered markdown/HTML rather than typed fresh.
That class of miss is mechanically fixable, and today it isn't fixed: the caller gets a correctly-formed "not found" and has to notice the encoding mismatch itself before retrying.

This design adds a one-shot fallback: when the literal input would fail, retry once against a normalized form, and if that retry is what succeeds, say so in the response.
A request that already matches literally is completely unaffected — this only changes what happens after today's logic has already decided to fail.

## Scope

| Tool | Parameter | Current match | Included | Why |
|---|---|---|---|---|
| `get_doc` | `section` | exact ordinal against server-issued heading paths | yes | A server-issued identifier compared by exact equality — the shape most likely to carry a copy-paste encoding artifact, and the case that motivated this design. |
| `lookup_api`, `find_api_references` | `symbol` | exact ordinal against known fully-qualified names | yes | Same shape as `section`. Generic names (`List<T>`) are the one place `<`/`>` appear in API symbols. |
| `search_docs` | `query`, non-regex only | substring/regex | yes (regex excluded) | A zero-hit result from an encoding slip looks identical to a real absence — the same silent-failure shape the project's no-silent-truncation rule exists to prevent. Regex mode is excluded: decoding a regex pattern can change what it matches in ways the caller did not ask for, and choosing `regex: true` is an explicit opt into regex semantics. |
| `search_api`, `search_api_text` | `pattern`, `query` | substring/segment match | yes | Same reasoning as `search_docs`. |
| `get_doc`, `get_doc_outline` | `path` | filesystem lookup, already case-insensitive on Windows | yes, low expected value | Paths come from directory listings, not rendered prose, so this specific mistake is far less likely to occur here. Included for consistency across the tool surface rather than because it is expected to fire. |
| — | `cursor`, `source`, `regex`, `limit`, `kind`, `exact` | opaque token / enum-like / numeric | no | Not free text matched against document content. `cursor` in particular is explicitly opaque and round-tripped verbatim — decoding it would corrupt it, not fix it. |

## Normalization rules

One shared normalizer, `Text/CallerInputNormalization.cs`, alongside but separate from `Text/DocumentationText.cs`.
It is a distinct seam for the opposite direction of data flow — caller to server, not source to caller — and reuses `DocumentationText.cs`'s file only by proximity, not by extending its responsibility.

It applies, in order, and reports whether anything actually changed:

- HTML entity decode (`System.Net.WebUtility.HtmlDecode`) — `&gt;`, `&lt;`, `&amp;`, `&quot;`, `&#39;`, and the rest of the standard entity set.
- Curly quotes to straight quotes (`'` `'` `'` → `'`; `"` `"` → `"`).
- Non-breaking space (U+00A0) to a regular space.

## Retry mechanics

Every included call site follows the same shape:

1. Try the literal input. This is today's code path, unchanged.
2. On not-found (exact-match sites) or zero hits (substring sites), ask the normalizer whether the input actually changes under the rules above. If it doesn't, fail exactly as today.
3. If it does change, retry once with the normalized value. If that retry also fails, fail exactly as today — normalization is strictly an additional attempt, never a replacement for the real answer.
4. If the retry succeeds, return that result, plus a note describing the substitution — following the existing per-result typed-note pattern (`search_api`'s `ApiSearchNote(string Message)` is the precedent), not a new shared or generic field. Each affected result type (`DocContentResult`, `DocSearchResult`, `ApiLookupResult`, `FindReferencesResult`, …) gets its own `…Note` record, consistent with how `ApiSearchResult.Note` already works, and `ApiSearchResult` in particular already carries a `Note` for dotted-pattern guidance — the two conditions are mutually exclusive by construction (guidance fires only on an empty result, normalization's note only on a successful retry), so no collision, but both live on the same field.

No new global input filter exists anywhere; the normalizer is only ever invoked from inside a failure path a caller would otherwise have hit.

## Transparency

Alongside the `note` field, `get_doc`'s response corrects a latent inconsistency: `DocContentResult.Section` currently echoes back whatever the caller passed, not the canonical `heading.Path` it matched against.
Today those are always identical, because exact match guarantees it, so switching the field to always report `heading.Path` is a no-op for every request that matches literally.
Once the fallback exists, it means a caller who sent the encoded form gets the correct form back in the response, not their own typo reflected at them.

## Error handling

If normalization doesn't change the input, or the retry also fails, the error is byte-for-byte what it is today: `section_not_found`, `path_not_found`, or a genuinely empty result set.
This feature only ever adds a second chance; it never changes the shape, wording, or triggering condition of an existing failure.

## Testing

- Unit tests on the normalizer: each rule independently, and a no-op case confirming untouched input is reported as unchanged.
- One regression test per included call site reproducing the motivating shape (an HTML-entity-encoded separator in a `section` value, against a document with a literal `>` in a real heading) and asserting both the successful result and the `note` field.
- One test per call site confirming `note` is absent when the literal input already matched — the fallback must be invisible on the common path, not just correct on the failure path.

## Known gaps

This design does not attempt to normalize URL-encoding (`%3E`), path-separator differences, or any mistake outside the HTML-entity and typographic set above.
If a future case surfaces a different encoding artifact, it extends the normalizer's rule list rather than requiring a new mechanism.

## Standing-record obligations

- `docs/decisions.md` — one entry recording the choice of a localized, retry-on-failure fallback over normalizing every string parameter unconditionally at the tool boundary (rejected: it would make a legitimately-authored `&gt;` in real heading text unreachable, and would leave nothing to report in `note` since the raw form would never survive to be compared).
- `docs/design/mcp-tool-surface.md` — the `section` and `symbol` entries (lines ~123–126, ~44) gain a clause noting the one-shot normalization fallback and the `note` field.
- `DocsTool.cs` and `ApiDocsTool.cs` — the affected tools' `[Description(...)]` strings gain a short clause, since that text is what the calling agent actually reads at call time, not this design document.
