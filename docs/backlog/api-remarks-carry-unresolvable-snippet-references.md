# API remarks carry snippet references nothing here can resolve

`lookup_api` returns a member's `remarks` whole and unprocessed. In `dotnet-api-docs` that prose is
Microsoft Learn markdown, and its examples are not inline — they are `:::code:::` tokens pointing at
files under `snippets/`:

```
:::code language="csharp" source="~/snippets/csharp/System/String/Contains/cont.cs" id="Snippet1":::
```

`sources.json` sparse-checks `dotnet-api-docs` out to `xml` alone, so `snippets/` is never fetched.
No tool in this server can resolve one of those paths, and `get_doc` cannot even be pointed at the
source: `dotnet-api-docs` is not a markdown source, so it answers `invalid_request`.

## Why it matters

The caller is an agent and `remarks` is the largest field a lookup returns. For
`System.String.Contains(string)` it is roughly 1 800 characters, of which six lines are `:::code:::`
tokens naming three snippet files in three languages, twice each. That is context spent on pointers
that resolve to nothing.

The second cost is worse than the wasted characters: the tokens read as an offer. An agent that
believes an example is one fetch away has two ways to be wrong — it calls `get_doc` and is told the
source is not markdown-searchable, or it decides the example exists and describes it.

`search_api_text` does not have this problem: its match text goes through `DocumentationText.Budget`
and is capped at 300 characters. Only the unbudgeted `remarks` on `lookup_api` carries the full
token soup.

## Evidence

- `sources.json`, `dotnet-api-docs`: `"sparse": ["xml"]`, and no `markdown` flag. Verified against a
  synced checkout at pin `a81557d3` — the working tree holds `xml/` and repository metadata, and no
  `snippets/` directory.
- `lookup_api(symbol: "System.String.Contains", source: "dotnet-api-docs")` against that pin returns
  `Contains (string value)` with a `remarks` containing six `:::code source="~/snippets/...":::`
  lines.
- `Features/ApiDocs/RepositoryApiDocsBackend.cs:224` budgets `search_api_text` match text to 300
  characters. No equivalent runs over `remarks`.

## Suggested fix

Decide what an unresolvable pointer should become, then apply it where API prose is rendered rather
than at the payload — the same placement argument `Text/DocumentationText.cs` already makes for
normalization.

The candidates, and what each costs:

- **Replace each token with a short marker** naming the language and that the source is not
  available locally. Keeps the fact that an example exists upstream, drops the false affordance,
  and costs a few characters instead of a line.
- **Drop them entirely.** Cheapest in context, and loses the signal that upstream documents an
  example at all.

Whichever is chosen, removing content from a payload must be reported rather than done silently —
that is the same obligation `isTruncated` and `skippedDeclarations` serve elsewhere. A count of
dropped references, or a flag on the member, keeps it honest.

Widening the sparse checkout to fetch `snippets/` is the option that makes the references real, and
it is the wrong trade: it would pull a large tree of sample projects into every user's cache to
serve a field that is already the most expensive one returned.
