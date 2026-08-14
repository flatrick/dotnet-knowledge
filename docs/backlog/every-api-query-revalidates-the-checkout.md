# Every API query revalidates the checkout with git

Before answering, each of the four API tools resolves the current snapshot through
`SourceSynchronizer.ReadCurrentSourceAsync`, which takes the per-source lock and runs three git
commands against the checkout — `rev-parse HEAD`, `config --get remote.origin.url`, and
`status --porcelain --untracked-files=all`. The package half repeats work too: `PackageApiCorpusStore.Read`
performs its bounded read, JSON preflight, deserialization and full structural validation *before*
consulting its cache, so the cache deduplicates the object graph but saves no I/O and no validation.

## Why it matters

The cost lands on every call, including an identical repeated one, and it is serialized per source
by the lock. `status --untracked-files=all` walks the whole working tree, which for
`dotnet-api-docs` is about 42 000 paths.

The check itself is worth having — it is what stops the server answering from a checkout that has
drifted from the recorded commit. What is not established is that it must run per query rather than
per sync.

## Evidence

Measured over one stdio session against a fully synchronized cache, taking the gap between
consecutive responses:

| Call | Elapsed |
|---|---|
| `lookup_api` (roslyn-api-docs) | ~1.0 s |
| `lookup_api` with `framework: net472` | ~1.0 s |
| `lookup_api` with `framework: NET8.0` | ~1.0 s |
| `search_api` across both API sources | ~1.3 s |
| `search_api_text` | ~1.6 s |
| `find_api_references` | ~1.5 s |

Repeating a query costs the same as issuing it the first time.

## Suggested fix

Measure which of the three git commands dominates before changing anything; `status` is the
suspect, and `rev-parse` alone may be enough to detect the case that matters. If the walk is the
cost, the cheap version is to trust the recorded commit within a session and re-validate only when
the generation directory's modification time changes.

Separately, move the corpus cache lookup ahead of the read and validation so a repeat query does not
re-read and re-validate the same file — the identity it is keyed on is already known before any of
that work starts.
