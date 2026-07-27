# Glob resolution is implemented twice

Two places resolve a VB project's `Compile` items to a file set:

- `tests/DotNetKnowledge.Corpus.Tests/Projects/VbSourceCoverageTests.cs` — `ResolveGlob`
- `scripts/verify-feature-floors.cs` — `ResolveGlob`

Both read `Include` and `Remove` attributes, require the `<directory>/**/*.<ext>` shape, throw
on any other shape, subtract removals from inclusions, and skip `bin` and `obj`.

## Why it matters

The two answer the same question for different consumers — one decides whether a row is
compiled by anything, the other decides which rows a project owns. If they drift, the corpus
gains a blind spot precisely where two guards appear to agree.

## Evidence

The implementations are behaviorally identical today; the only textual difference is a
redundant `Path.GetFullPath` in the script, which is a no-op because both canonicalize the
directory first.

The duplication is deliberate and currently unavoidable: a test project and a standalone
single-file script cannot share code, and this repository's convention is that tooling is
single-file C# with no shared library.

## Suggested fix

None obviously better than the status quo. Both throw loudly if the glob shape changes, and
neither can silently under-count, which bounds the damage.

If the corpus ever grows a third consumer of this logic, that is the point to reconsider — for
example by having the script emit the resolved sets and the test assert against that output,
so one implementation feeds both.

Until then, treat the pair as a unit: a change to one is a change to both.
