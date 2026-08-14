# API package supplements are limited to the Roslyn cohort

`apiPackages` can only carry packages that ship as part of the Roslyn cohort the `roslyn-api-docs`
checkout describes. Two independent checks enforce it, and a package versioned on its own schedule
fails the second at synchronization time.

## Why it matters

The four API tools answer from the package half for anything the documentation checkout does not
cover, so the catalog decides the whole of that coverage. Restricting it to one cohort means the
server can answer about `Microsoft.CodeAnalysis.*` and nothing else, however adjacent — including
the MSBuild types `MSBuildWorkspace` hands its callers, which is the documented reason the workspace
package is pinned at all.

It also blocks the one package measured to make the `framework` argument observable.
`Microsoft.Build.Framework` 18.4.0 declares 213 things on `net472` that do not exist on `net10.0` —
the `XamlTypes` namespace among them, because it needs the .NET Framework-only `System.Xaml`. Every
package the server *can* catalog has one surface across all its frameworks.

## Evidence

- `ApiPackageGenerationContributor.AppliesToSource` requires `definition.Repository` to equal
  `dotnet/roslyn-api-docs`, so no other source can carry `apiPackages` at all.
- `RoslynPackageCohort.Read` reads `dotnet/xml/PackageInformation/roslyn-dotnet-<version>.json` from
  the checkout and, for a pinned ref, rejects the catalog unless **every** entry's `version` equals
  the cohort's. Adding `Microsoft.Build.Framework` 18.4.0 beside
  `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.3.0 fails with
  `The Roslyn package cohort '5.3.0' does not match the pinned package catalog.`
- The failure is at sync time, not catalog-load time, so a bad entry parses, passes the corpus
  builder when driven directly, and only fails when `sync_source` runs.

## Suggested fix

Not obvious, which is why this is deferred. The cohort check is worth keeping for cohort packages:
it is what stops the pinned assemblies drifting from the XML documentation they are joined to. What
is missing is a way to say a package is *not* part of the cohort and is pinned on its own.

The smallest honest version is a per-package opt-out — an explicit `cohort: false`, or an absent
entry in the manifest meaning "independently versioned" — plus relaxing `AppliesToSource` so a
source other than `roslyn-api-docs` can carry packages. Both halves need deciding together, because
a package outside the cohort also has no documentation checkout to merge with, and
`ApiDocsQueryService` currently assumes the repository half wins on overlap.

Reproduce by adding any independently versioned package to `apiPackages` and running `sync_source`;
`scripts/probes/diff-tfm-surface.cs` measures what such a package would add.
