# Unfiltered document search has no relevance signal

`DocRanking.Order` sorts by document tier, then by whether the match landed on a heading,
then by path ordinal, line, and repo. Nothing in that chain measures how well a document
answers the query. `Order` even takes the query as a parameter and does not read it.

## Why it matters

A `search_docs` call without `source` fans out across four sources. Within a tier, which
source leads is decided by how its paths sort alphabetically — `docs/` before `proposals/`
before `spec/` — which is unrelated to what the caller asked.

The tiering added with `nuget-docs` works around the worst case rather than solving it: NuGet
guidance is deliberately ranked below language proposals precisely because the tiebreak below
that point is arbitrary. That is a workaround holding a real gap closed.

## Evidence

- `DocRanking.Order`'s `query` parameter is documented as "accepted for symmetry with the
  other rankers and to leave room for query-dependent weighting; today the ordering is driven
  by the hit's path and text alone."
- `ApiTextRanking` and `ApiSearchRanking` do use their query. The document ranker is the
  outlier.

## Suggested fix

Score a hit against the query before falling through to path ordering: whole-query match over
partial, match in a heading over match in prose (already partly there), match in the section
path over match in body text. Keep the tiers — they encode document authority, which is a
different quantity from relevance and should not be collapsed into it.
