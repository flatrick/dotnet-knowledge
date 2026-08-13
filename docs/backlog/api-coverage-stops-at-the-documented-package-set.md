# API coverage stops at the documented package set

`lookup_api`, `search_api`, `search_api_text` and `find_api_references` read the ECMA XML in
`roslyn-api-docs` and `dotnet-api-docs`. Those repositories document a fixed list of packages,
and an API outside it does not exist as far as this server is concerned.

The covered Roslyn set is the eight packages named in the newest manifest under
`<cacheDir>/dotnet/xml/PackageInformation/roslyn-dotnet-<version>.json`:
`Microsoft.CodeAnalysis`, `.Common`, `.CSharp`, `.CSharp.Workspaces`, `.VisualBasic`,
`.VisualBasic.Workspaces`, `.Workspaces` and `.Workspaces.Common`.

`Microsoft.CodeAnalysis.Workspaces.MSBuild` is not among them, so the entire
`Microsoft.CodeAnalysis.MSBuild` namespace is unreachable — `MSBuildWorkspace`, its four `Create`
overloads, `MSBuildProjectLoader`.

## Why it matters

The failure is silent and it points the wrong way. `lookup_api` on an uncovered symbol returns
`not_found` with the message *"was not found in the selected synchronized source(s). Call search_api
with a type-name fragment to find candidates."* — which describes a **mistyped name**, and invites a
retry that cannot succeed. Nothing distinguishes "you spelled it wrong" from "this package is not in
the corpus".

Both readings were live in one session: `Microsoft.CodeAnalysis.SymbolFinder.FindCallersAsync`
failed because the type is in `Microsoft.CodeAnalysis.FindSymbols`, and
`Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create` failed because the package is uncovered.
The two responses were identical, so the second was initially misread as a server defect.

The gap also lands on a real consumer. The `dotnet-mcp` server is built on `MSBuildWorkspace`, and
its rules point every API-behavior claim at this server first — so the one namespace it most needs
to check is the one with no coverage.

Escalating to the web does not rescue it. Microsoft never published that namespace to the Learn API
browser either: `learn.microsoft.com/dotnet/api/microsoft.codeanalysis.msbuild` and its
`msbuildworkspace` child both return 404, and a Learn search for the type returns neighbors only.

## Evidence

- `search_api pattern="MSBuildWorkspace"` and `pattern="Microsoft.CodeAnalysis.MSBuild"` both return
  `items: []`.
- `<cacheDir>/roslyn-api-docs/dotnet/xml/` holds 31 directories; no `Microsoft.CodeAnalysis.MSBuild`.
- `rg -l 'CodeAnalysis\.MSBuild'` over the whole `roslyn-api-docs` cache matches zero files.
- `roslyn-dotnet-5.3.0.json` lists the eight packages above and no MSBuild package.
- The `dotnet/roslyn-api-docs` README states the source of truth is the `///` comments in the Roslyn
  repositories — the same comments the NuGet package ships as XML.

## Suggested fix

Two independent parts. The second is the interesting one.

**Make the empty result self-describing.** Whatever else happens, `not_found` should distinguish an
unresolvable name from an uncovered package: report the covered package set, or note that the
requested namespace's assembly is outside it. A message that only suggests a retry teaches the
caller to doubt their spelling when the corpus is what is missing.

**Consider a NuGet-package XML source.** Every package ships its `///` comments as
`lib/<tfm>/<AssemblyName>.xml` beside the assembly, in the same ECMA member-id vocabulary
(`M:Namespace.Type.Member(ParamType)`) the existing lookup already speaks. That is a plausible
second API-doc backend covering everything the two docs repositories omit, and for a
version-sensitive question it is *more* authoritative than they are: it is the exact build the
consumer resolved, not whatever upstream last generated.

What makes it a design question rather than a patch:

- **Where do packages come from?** Reading a machine's `~/.nuget/packages` makes results depend on
  local state, which every existing source deliberately avoids by pinning to a commit. Restoring
  declared packages into a server-owned cache keeps the pinning property but adds a fetch path and a
  trust boundary this server does not have today.
- **Which version answers?** Several versions of one package coexist in a cache. Answering from the
  wrong one is a plausible-looking wrong answer, which is worse than `not_found`. A consumer's
  `Directory.Packages.props` knows, but the server currently has no notion of a consumer project.
- **The shape is thinner than ECMA XML.** A package's XML carries `summary`, `param`, `returns`,
  `remarks` — but no signatures, no inheritance, no attributes. `lookup_api` returns signatures
  today, so a package-backed match would have to either synthesize them from the assembly's metadata
  or return a visibly reduced record. It must not silently look like the richer one.
- **Provenance has no commit.** Every response carries `repo`/`ref`/`commit`. A package-backed hit
  would carry a package id and version instead, so the provenance contract needs a second shape
  rather than a forced fit.

A narrow first cut worth costing: index only packages named in an explicit server-side list, resolve
from the existing NuGet cache, and mark every such match with its own provenance kind and a
`detail` that says signatures are absent. That answers the `MSBuildWorkspace` case without taking on
restore, version negotiation, or metadata reading.
