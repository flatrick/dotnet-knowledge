# Framework selection has no observable effect on any measured package

The four API tools take a `framework`, `sync_source` normalizes one corpus per target framework,
and cursors bind the framework they were minted under. Across every Roslyn package measured, all of
a package's frameworks produce the same public API surface, so the argument selects between
identical datasets and no answer an agent receives depends on it.

## Why it matters

The argument is not free. It is documented on all four tools, it has its own
`framework_not_available` failure, it participates in cursor identity, and it multiplies the
normalization work and the stored corpora by the number of target frameworks. A caller has to
reason about a choice that currently cannot change a result.

It is also not obviously wrong: a package whose public surface really does vary by target framework
would need exactly this, and the mechanism is cheap to keep and expensive to re-add. What is missing
is a package that demonstrates the need.

## Evidence

Public types and members read by `MetadataApiReader` per `lib/<tfm>/` assembly, compared pairwise:

| Package | Frameworks | Public types | Public members | Difference |
|---|---|---|---|---|
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.3.0 | 4 | 6 | — | none |
| `Microsoft.CodeAnalysis.Common` 5.6.0 | 3 | 453 | 3921 | none |
| `Microsoft.CodeAnalysis.Workspaces.Common` 5.6.0 | 3 | 147 | 1733 | none |
| `Microsoft.CodeAnalysis.CSharp` 5.6.0 | 3 | 324 | 6046 | none |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` 5.6.0 | 3 | 3 | 55 | none |

Seventeen framework-specific assemblies, no public API difference in any of them.

The compiler XML does differ per framework for `Common` and `Workspaces.Common`, which is what makes
the result worth recording rather than assuming: the differing entries are internal
(`Collections.Internal`, private fields, polyfill attributes such as `IsExternalInit`, generated
regex classes) and correctly never reach a public-API corpus. For the pinned MSBuild package the
whole difference between `net472` and the rest is two members on the internal
`Microsoft.CodeAnalysis.NamedPipeUtil`.

For the pinned package this is also visible in the published corpora: `net10.0.json`, `net472.json`,
`net8.0.json` and `net9.0.json` are byte-identical once the framework name is normalized away
(50 140 characters each).

The resource cost is small and is not the reason to act: the `package-normalize` stage takes about
180 ms for all four frameworks, and the four corpora total roughly 200 KB.

## Suggested fix

Do not remove it on this evidence alone — Roslyn maintains one public API baseline per project
rather than per target framework, so these packages may be unrepresentative of packages in general,
and every package measured so far is a Roslyn one. Re-measure when the first non-Roslyn package is
cataloged.

If nothing ever diverges, removal touches all four tools' parameters, the
`framework_not_available` error, `ApiQueryCoverage`, `NuGetProvenance.Framework`, the cursor scope
and `docs/design/mcp-tool-surface.md`. The cheaper intermediate step is to keep the query surface and
have `PackageApiCorpusBuilder` store one corpus per distinct content with frameworks mapping onto it,
which removes the duplication without deciding the question.

Reproduce with `scripts/probes/diff-tfm-surface.cs` against any package directory containing `lib/`.
