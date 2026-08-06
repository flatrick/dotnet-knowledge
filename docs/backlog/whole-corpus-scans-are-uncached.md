# `search_api_text` and `find_api_references` re-read the whole corpus per query

Both tools scan every XML file in every selected source on every call. Nothing is retained between
calls: two identical queries in a row cost the same as one.

Measured against the pinned corpus (11,359 files, roughly 460 MB in `dotnet-api-docs`):

| Operation | Cost |
|---|---|
| Parallel raw-text prefilter, whole corpus | 0.5-0.7 s |
| The same serially | 2.1 s |
| `search_api_text` end to end through the MCP host | 0.7-3.6 s |
| `find_api_references` end to end | 0.7-1.8 s |

## Why it matters

It does not, yet — the numbers are inside what an agent will wait for, and this is recorded so the
cost is a known quantity rather than something rediscovered later under worse conditions.

Two things would change that. A source substantially larger than `dotnet-api-docs`, and a client that
issues several of these per turn, which is the expected usage: the tools are designed to be called
speculatively and then narrowed.

The measurements above are also warm — the OS file cache is populated by the time they run. A cold
first query on a machine that has just synced will be slower, and that has not been measured.

## Evidence

`ReadTextSource` and `ReadReferenceSource` each build their file list with
`Directory.EnumerateFiles(docsRoot, "*.xml", SearchOption.AllDirectories)` and read every entry
through `File.ReadAllText` inside a `Parallel.ForEach`. Neither consults nor populates any store.

## Suggested fix

Not an index. `docs/design/mcp-tool-surface.md` records why: an index puts a build step between a
sync and a correct answer, and a stale one answers with plausible absences.

The cheap options preserve that property. The prefilter's inputs are a source's commit and the file
tree, both already known and both immutable while a source stays pinned, so a first-token-to-files
map could be built lazily on first use and discarded when `SourceSyncState.Commit` changes — derived
from the same read that answers the query, never a separate build. Failing that, memoizing the most
recent query's surviving file list would collapse the common narrow-then-repeat pattern for nothing.

Measure a cold run before deciding either is worth it.
