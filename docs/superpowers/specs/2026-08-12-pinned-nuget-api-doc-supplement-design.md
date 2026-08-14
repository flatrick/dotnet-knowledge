# Pinned NuGet API-documentation supplement

## Purpose

Extend Roslyn API coverage to `Microsoft.CodeAnalysis.Workspaces.MSBuild`, whose XML documentation
is shipped in its NuGet package but omitted from `dotnet/roslyn-api-docs`. The package is a pinned
supplement to the existing `roslyn-api-docs` source, not a general-purpose package browser.

The first configured package is `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.3.0 because 5.3.0 is
the Roslyn package cohort described by the newest manifest in the repository's pinned commit. The
design deliberately permits another explicitly configured package later, but it does not discover,
restore, or answer from arbitrary packages or consumer projects.

The supplement reads both artifacts that define the answer:

- the package XML supplies summaries, parameters, returns, remarks, and other authored prose;
- the matching assembly supplies the actual visible API, C# signatures, inheritance, attributes,
  constraints, and structural type references.

All package target frameworks are available to callers. The package currently carries `net472`,
`net8.0`, `net9.0`, and `net10.0`; `net10.0` is the explicit default.

## Non-negotiable behavior

- A query never downloads a package. Only `sync_source` performs network work.
- Package identity, version, content hash, feed, framework, and fetch time travel with every
  package-backed match.
- The server never loads or executes a synchronized assembly. It reads metadata only.
- A failed synchronization cannot publish a partial package corpus.
- Repository API documentation remains preferred wherever it overlaps package documentation.
- No Microsoft package, XML documentation, assembly, or generated derivative is committed here.
- An absent result describes the searched coverage instead of asserting that the name is misspelled.

## Catalog model

`roslyn-api-docs` gains an API-package supplement declaration in `sources.json`. The first entry
names:

- package ID `Microsoft.CodeAnalysis.Workspaces.MSBuild`;
- assembly basename `Microsoft.CodeAnalysis.Workspaces.MSBuild`;
- feed `https://api.nuget.org/v3/index.json`;
- pinned cohort version `5.3.0`;
- the expected NuGet SHA-512 for that package version;
- default framework `net10.0`.

The version is intentionally duplicated beside the Git pin. Catalog validation compares it with
the newest `dotnet/xml/PackageInformation/roslyn-dotnet-<version>.json` manifest in the synchronized
pinned repository. A mismatch fails synchronization: silently combining two Roslyn releases would
produce plausible but wrong answers.

For the explicit `head` synchronization mode, upstream drift is already part of the existing
contract. The synchronizer derives the cohort version from the synchronized commit's newest
manifest, downloads that package version, and records the observed SHA-512 in provenance. The
catalog's pinned version and expected hash apply only to the vouched-for pinned mode. If the cohort
is inconsistent, the package is unavailable, or no matching manifest exists, head synchronization
fails rather than publishing a Git-only corpus with silently reduced coverage.

The feed is a NuGet v3 service index. Synchronization resolves its `PackageBaseAddress` resource and
downloads the exact package ID and version. It does not run `dotnet restore`, consult the user's
global package cache, or honor machine NuGet configuration. Those paths would make identical server
pins produce different answers on different machines.

## Composite synchronization

`roslyn-api-docs` becomes a composite synchronized source:

1. Stage the requested Git revision and validate its Roslyn package cohort.
2. Fetch the exact `.nupkg` into a temporary server-owned location.
3. Verify its SHA-512 before opening the archive.
4. Locate every `lib/<tfm>/<configured-name>.dll` and matching `.xml` pair.
5. Normalize each pair into a deterministic framework corpus.
6. Validate that the configured default framework exists.
7. Publish the Git state, package state, and normalized corpora as one complete generation.

Readers resolve one immutable current-generation pointer. Publishing replaces that pointer only
after every staged component passes validation, so a failed refresh leaves the prior generation
readable. Cancellation follows the same rule. Temporary generations are cleaned up on a later sync
or startup and are never treated as current.

`list_sources` retains the Git status of `roslyn-api-docs` and adds a `supplements` collection. Each
supplement reports package ID, version, synchronization status, verified hash, available
frameworks, and default framework. `sync_source` returns the same composite state after a successful
sync. An agent does not need filesystem access to diagnose package coverage.

## Trust boundary

A NuGet package is untrusted input even when its expected hash is pinned. Before normalization, the
synchronizer:

- rejects absolute archive paths, parent traversal, duplicate normalized paths, and paths outside
  the package staging directory;
- applies limits to individual uncompressed entries and total uncompressed content;
- accepts only the configured assembly/XML basename under `lib/<tfm>` as API inputs;
- rejects a missing pair, duplicate pair, assembly-name mismatch, malformed XML, or malformed
  metadata;
- never executes install scripts, targets, analyzers, tools, or assemblies from the package.

Hash verification occurs before archive parsing. Package content is read from the staged archive;
it is not extracted into the Git checkout or repository working tree.

## Internal API corpus

The query layer consumes an internal API-corpus model rather than a specific XML schema. Two
readers implement that boundary:

- the repository reader adapts the existing per-type ECMA XML files;
- the package reader consumes a normalized framework corpus generated from package XML and assembly
  metadata.

This does not normalize the large Git repositories during synchronization. Their current on-disk
layout remains the source of truth. Only the small package supplement is persisted in the derived
format.

The package normalization pipeline has four focused components:

1. **Package asset reader** discovers and validates an XML/DLL pair for each target framework.
2. **Metadata reader** uses `System.Reflection.Metadata` to enumerate declarations and decode type
   signatures without loading the assembly.
3. **XML-doc reader** parses ECMA member IDs and documentation elements.
4. **Normalizer** joins declarations and documentation by ECMA ID and writes one deterministic
   corpus per framework.

The corpus records:

- canonical namespace, type, and member identities;
- member kind, accessibility, and C# signature;
- summary, remarks, parameters, type parameters, returns, value, exceptions, and resolved reference
  text needed by documentation search;
- base types, interfaces, generic constraints, parameter and return types, and attributes needed by
  `find_api_references`;
- target framework and package provenance.

Assembly metadata decides which APIs exist. Public and protected API is included, including
protected-internal declarations; private, internal-only, and private-protected declarations are
excluded. A visible declaration without an XML entry remains searchable and has null documentation
fields. An XML entry without matching visible metadata is not exposed as API.

The signature formatter produces the same C#-oriented surface the current tools promise. It covers
constructors, methods, operators, properties, indexers, fields, events, nested and generic types,
generic constraints, arrays, pointers, nullable metadata, tuples, and `ref`/`in`/`out` modifiers.
Unsupported or malformed metadata fails normalization instead of emitting an approximate
signature.

One deterministic schema-version-2 corpus file is stored per framework. Schema 2 retains both the
rendered base/interface expression and its canonical contained type identities. A schema mismatch
requires source resynchronization; generated corpus files are not migrated. A process-local cache
may retain a decoded corpus keyed by package ID, version, hash, and framework; that cache is an
optimization and never a source of identity.

## Tool contract

`lookup_api`, `search_api`, `search_api_text`, and `find_api_references` each gain:

```text
framework?: string
```

The parameter has these rules:

- omitted: use the configured default, `net10.0`;
- a supported TFM: select exactly that package asset;
- unsupported TFM: return `framework_not_available` with `availableFrameworks` and
  `defaultFramework`;
- supplied with `source: "dotnet-api-docs"`: reject it as inapplicable rather than silently ignore
  it;
- supplied with `source: "roslyn-api-docs"` or with no source restriction: apply it to the package
  supplement while Git-backed results remain framework-neutral.

Framework names are matched case-insensitively and returned in their canonical package spelling.
The default is explicit catalog data, not a runtime attempt to order arbitrary TFMs.

When `roslyn-api-docs` participates, responses include `effectiveFramework` and
`availableFrameworks`. Pagination cursors bind to every searched Git revision, the package ID,
version and SHA-512, and the effective framework. Any change rejects the cursor as stale rather
than reading it against different API data.

### Merge behavior

The package is a supplement, not a peer authority:

1. Read repository-backed matches.
2. Read package-backed matches for the effective framework.
3. Merge by canonical type and member identity.
4. Keep the repository declaration and documentation on an overlap.
5. Add package types and members absent from the repository corpus.

The same rule applies to name search, documentation-text search, and structural-reference search,
so a duplicate declaration cannot consume two result slots or inflate totals. Package-only members
of a repository-covered type may supplement that type; precedence is per declaration rather than
per file.

### Provenance

Source provenance becomes a discriminated wire shape. Git-backed values retain every existing field
and add a discriminator. These examples use the repository's current pins and the package's current
NuGet hash; the fetch times only illustrate the wire format:

```json
{
  "kind": "git",
  "repo": "dotnet/roslyn-api-docs",
  "ref": "pinned",
  "commit": "4fed37999ba2fb00b90d4489358bb9a7763e49ef",
  "fetchedAt": "2026-08-12T10:00:00Z"
}
```

Package-backed values use package-native identity:

```json
{
  "kind": "nuget",
  "packageId": "Microsoft.CodeAnalysis.Workspaces.MSBuild",
  "version": "5.3.0",
  "sha512": "eA4XuxeicHbppkEcCv1sxGqdyEcrYisH1tUqTvN9pehiQKURoIdR7ydohK6WUoenjTJNJDMH3HqAP6P1Vu/yRg==",
  "feed": "https://api.nuget.org/v3/index.json",
  "framework": "net10.0",
  "fetchedAt": "2026-08-12T10:00:00Z"
}
```

No package hash is forced into a field named `commit`, and no NuGet feed is presented as a Git
repository. Mixed responses keep provenance on each match and report both identities among searched
sources.

### Empty results and failures

A final `not_found` response lists the exact searched Git sources, package ID/version/hash, and
framework. Its message says that the requested name is either invalid or outside the stated
coverage. It does not direct the caller to repeat an API search as if spelling were the only
possible cause.

Synchronization fails without changing the current generation on:

- feed or download failure;
- package hash mismatch;
- Roslyn cohort mismatch;
- missing or duplicate XML/DLL assets;
- absent configured default framework;
- archive-boundary violation;
- malformed XML or assembly metadata;
- assembly-name mismatch;
- normalization failure.

A query against a generation without its required supplement fails fast as `source_not_synced` and
names `sync_source(name: "roslyn-api-docs")` as the remedy. It never falls back to a plausible
Git-only absence.

## Testing

Automated tests use repository-authored fixture source, XML, and assemblies. They do not fetch
NuGet.org and do not commit any Microsoft package content. A package transport boundary accepts a
local test implementation so synchronization tests can construct packages without network access.

The existing test suite covers:

- catalog validation for package identity, pinned hash, default framework, and Roslyn cohort;
- NuGet v3 service-index and package-address handling through a fake transport;
- hash mismatch, traversal, duplicate paths, size limits, malformed inputs, and assembly mismatch;
- discovery of multiple target frameworks and exact default-framework selection;
- public/protected visibility and exclusion of non-public API;
- XML/metadata joins by ECMA ID, including visible undocumented declarations;
- C# rendering for overloads, generics, nested types, arrays, nullable metadata, tuples,
  `ref`/`in`/`out`, properties, indexers, events, operators, and constraints;
- documentation extraction and reference-text normalization;
- base, interface, constraint, parameter, return, and attribute references;
- repository-wins merging and package-only members on an otherwise overlapping type;
- framework-specific API differences;
- package provenance, coverage reporting, and cursor invalidation;
- atomic rollback after failed or cancelled synchronization;
- `list_sources`, `sync_source`, and all four API tools at the protocol boundary.

An optional local smoke command may query the real pinned 5.3.0 package, but it is not the only test
of any behavior. No separate test suite is introduced; the repository's normal local test command
runs all deterministic coverage.

## Rejected approaches

### Read package artifacts directly during every query

This avoids a derived cache but spreads package-specific parsing, caching, and pagination behavior
across all four API operations. Normalizing once at synchronization keeps queries bounded and makes
one format boundary responsible for correctness.

### Recreate the `roslyn-api-docs` per-type XML layout

This maximizes reuse of the current parser, but it makes this server reproduce an upstream format
whose richer signature and structural elements are not present in package XML. A purpose-built
internal corpus is smaller, explicit, and under this repository's control.

### Read the user's global NuGet package cache

Machine state would choose both availability and potentially version, so two installations with the
same source pins could answer differently. The synchronized source owns its cache and download.

### Restore packages from a consumer project

Project-resolved versions are valuable but require project context, NuGet configuration, restore,
and a different provenance contract. This design intentionally answers from the Roslyn API-doc
cohort instead.

### Build documentation from the Roslyn source repository

Building Roslyn and its documentation toolchain is substantially slower and more brittle than
reading the exact XML and assembly Microsoft already ships. It adds no fidelity needed by the four
query tools.

## Documentation obligations

- Update `docs/design/mcp-tool-surface.md` for `framework`, composite synchronization, coverage
  reporting, and discriminated provenance.
- Update `README.md` and the implemented-tool descriptions for the new coverage.
- Add the normal verification command to `CLAUDE.md` only if test invocation changes; this design
  does not require a new suite.
- Delete `docs/backlog/api-coverage-stops-at-the-documented-package-set.md` when the implemented
  behavior closes the documented gap, and remove its row from `docs/backlog/README.md`.
- Append the accepted package-source, framework-selection, and provenance decisions to
  `docs/decisions.md`; do not edit earlier entries.
