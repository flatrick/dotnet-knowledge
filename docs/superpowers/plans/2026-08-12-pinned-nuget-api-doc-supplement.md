# Pinned NuGet API-Documentation Supplement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make all four API tools query the pinned `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.3.0 XML documentation and assembly metadata for a caller-selected package framework, defaulting to `net10.0`.

**Architecture:** Extend `roslyn-api-docs` with one cataloged NuGet supplement. `sync_source` downloads and verifies the package, normalizes each XML/DLL asset pair into a deterministic internal corpus, and atomically publishes the Git checkout and supplement as one generation. API queries merge repository and package records through a common backend contract, prefer repository declarations on overlap, and carry discriminated Git or NuGet provenance.

**Tech Stack:** .NET 10, C# 14, `System.Net.Http`, `System.IO.Compression`, `System.Reflection.Metadata`, `System.Text.Json`, MCP C# SDK 2.0.0, MSTest 4.3.2.

## Global Constraints

- The only initial package is `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.3.0, aligned with the newest Roslyn package manifest in the pinned `roslyn-api-docs` commit.
- The expected pinned NuGet SHA-512 is `eA4XuxeicHbppkEcCv1sxGqdyEcrYisH1tUqTvN9pehiQKURoIdR7ydohK6WUoenjTJNJDMH3HqAP6P1Vu/yRg==`.
- Expose `net472`, `net8.0`, `net9.0`, and `net10.0`; omission selects the cataloged `net10.0` default.
- Query tools never download. Only explicit `sync_source(name: "roslyn-api-docs")` performs package network work.
- Never load or execute package assemblies; use `System.Reflection.Metadata` only.
- Verify SHA-512 before opening the archive, validate archive paths and size budgets, and publish no partial state.
- Read no package from the user's global NuGet cache and honor no machine NuGet configuration.
- Repository documentation wins per canonical declaration when Git and package records overlap.
- Do not commit Microsoft package content, XML, assemblies, or generated corpora.
- Preserve the user's currently uncommitted backlog entry and index row until Task 11; do not stage them in earlier task commits.
- Preserve explicit pagination, bind cursors to Git/package/framework identity, and never silently truncate.
- All identifiers, comments, and prose use American English.

## File Structure

- `Sources/ApiPackageDefinition.cs` owns catalog and synchronized package identity.
- `Sources/SourceSnapshot.cs` owns immutable current-generation paths.
- `Sources/NuGetPackageClient.cs` owns NuGet v3 download and hash verification.
- `Sources/PackageArchiveReader.cs` owns archive validation and framework asset discovery.
- `Sources/RoslynPackageCohort.cs` owns manifest selection and version alignment.
- `Features/ApiDocs/Corpus/` owns normalized models, metadata reading, XML reading, building, and storage.
- `Features/ApiDocs/ApiDocsBackend.cs` defines the common repository/package query boundary.
- `Features/ApiDocs/RepositoryApiDocsBackend.cs` preserves existing repository behavior.
- `Features/ApiDocs/PackageApiDocsBackend.cs` queries normalized package corpora.
- `tests/DotNetKnowledge.Mcp.Tests.ApiFixture/` supplies repository-authored DLL/XML fixtures.

Existing `ApiDocsQueryService.cs` remains the coordinator but loses file-format-specific package concerns. Existing repository matching algorithms move behind a backend without behavioral rewrites.

---

### Task 1: Catalog the pinned package supplement

**Files:**
- Create: `src/DotNetKnowledge.Mcp/Sources/ApiPackageDefinition.cs`
- Modify: `src/DotNetKnowledge.Mcp/Sources/SourceCatalog.cs`
- Modify: `sources.json`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Sources/SourceCatalogTests.cs`

**Interfaces:**
- Produces: `ApiPackageDefinition` and `SourceDefinition.ApiPackages` for Tasks 3, 6, and 8.

- [ ] **Step 1: Write failing catalog tests**

```csharp
[TestMethod]
public void BundledRoslynSourcePinsTheMsbuildApiPackage()
{
    var packages = new SourceCatalog().Sources["roslyn-api-docs"].ApiPackages;
    Assert.HasCount(1, packages);
    var package = packages[0];
    Assert.AreEqual("Microsoft.CodeAnalysis.Workspaces.MSBuild", package.PackageId);
    Assert.AreEqual("5.3.0", package.Version);
    Assert.AreEqual("net10.0", package.DefaultFramework);
}
```

Add data rows proving that an empty package ID, path-bearing assembly name, non-HTTPS feed, nonnumeric version, non-64-byte base64 hash, empty default framework, and duplicate case-insensitive package IDs each throw `InvalidDataException`.

- [ ] **Step 2: Verify the tests fail**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter FullyQualifiedName~SourceCatalogTests --nologo`

Expected: compilation fails because `ApiPackages` does not exist.

- [ ] **Step 3: Add the catalog type and validation**

```csharp
public sealed record ApiPackageDefinition(
    [property: JsonPropertyName("packageId")] string PackageId,
    [property: JsonPropertyName("assemblyName")] string AssemblyName,
    [property: JsonPropertyName("feed")] string Feed,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("sha512")] string Sha512,
    [property: JsonPropertyName("defaultFramework")] string DefaultFramework);
```

Add nullable JSON property `apiPackages` to `SourceDefinition`, normalize it to an empty list, and implement every validation asserted in Step 1 without adding a NuGet client dependency.

- [ ] **Step 4: Add the exact package declaration**

```json
"apiPackages": [{
  "packageId": "Microsoft.CodeAnalysis.Workspaces.MSBuild",
  "assemblyName": "Microsoft.CodeAnalysis.Workspaces.MSBuild",
  "feed": "https://api.nuget.org/v3/index.json",
  "version": "5.3.0",
  "sha512": "eA4XuxeicHbppkEcCv1sxGqdyEcrYisH1tUqTvN9pehiQKURoIdR7ydohK6WUoenjTJNJDMH3HqAP6P1Vu/yRg==",
  "defaultFramework": "net10.0"
}]
```

- [ ] **Step 5: Pass tests and commit**

Run the Step 2 command; expected: PASS.

```powershell
git add sources.json src/DotNetKnowledge.Mcp/Sources/ApiPackageDefinition.cs src/DotNetKnowledge.Mcp/Sources/SourceCatalog.cs tests/DotNetKnowledge.Mcp.Tests/Sources/SourceCatalogTests.cs
git commit -m "feat: catalog pinned Roslyn API package supplement"
```

---

### Task 2: Publish source snapshots as immutable generations

**Files:**
- Create: `src/DotNetKnowledge.Mcp/Sources/SourceSnapshot.cs`
- Modify: `src/DotNetKnowledge.Mcp/Sources/SourceSyncState.cs`
- Modify: `src/DotNetKnowledge.Mcp/Sources/SourceSyncResult.cs`
- Modify: `src/DotNetKnowledge.Mcp/Sources/SourceCache.cs`
- Modify: `src/DotNetKnowledge.Mcp/Sources/SourceSynchronizer.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsQueryService.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Sources/SourceCacheTests.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Sources/SourceSynchronizerTests.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/Sources/SourcesToolTests.cs`

**Interfaces:**
- Produces: `SourceSnapshot` and `ISourceGenerationContributor`.
- Changes: `ReadCurrentSourceAsync<T>` callback to `Func<SourceSnapshot,T>`.

- [ ] **Step 1: Write failing generation-publication tests**

Assert the first sync publishes `.generations/<source>/<generation>/repository`; state names the generation; a failed contributor retains the old commit and directory; and readers see only a complete old or new generation.

```csharp
var before = await synchronizer.TryGetCurrentSnapshotAsync("docs", CancellationToken.None);
contributor.Failure = new InvalidOperationException("fixture failure");
await Assert.ThrowsExactlyAsync<InvalidOperationException>(
    () => synchronizer.SyncAsync("docs", null, CancellationToken.None));
var after = await synchronizer.TryGetCurrentSnapshotAsync("docs", CancellationToken.None);
Assert.AreEqual(before!.GenerationDirectory, after!.GenerationDirectory);
```

- [ ] **Step 2: Verify the tests fail**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter "FullyQualifiedName~SourceCacheTests|FullyQualifiedName~SourceSynchronizerTests|FullyQualifiedName~SourcesToolTests" --nologo`

Expected: compilation fails because snapshots and contributors are absent.

- [ ] **Step 3: Add exact snapshot contracts**

```csharp
public sealed record SourceSnapshot(
    SourceDefinition Definition,
    SourceSyncState State,
    string GenerationDirectory,
    string RepositoryDirectory,
    string SupplementsDirectory);

public interface ISourceGenerationContributor
{
    bool AppliesTo(SourceDefinition definition);
    Task<IReadOnlyList<ApiPackageSyncState>> BuildAsync(
        SourceDefinition definition, string refLabel, string repositoryDirectory,
        string supplementsDirectory, IProgress<string>? progress,
        CancellationToken cancellationToken);
}

public sealed record ApiPackageSyncState(
    string PackageId, string AssemblyName, string Version, string Sha512,
    string Feed, DateTimeOffset FetchedAt, string DefaultFramework,
    IReadOnlyList<string> AvailableFrameworks, string CorpusDirectory);
```

Bump state schema to 2 and add `Generation` and `ApiPackages`. Add cache helpers for generation, repository, and supplement paths.
Add `ApiPackages` to `SourceSyncResult`. Keep the public two-argument synchronizer constructor using an empty contributor list; add an internal constructor accepting contributors for tests, while DI supplies `IEnumerable<ISourceGenerationContributor>`.

- [ ] **Step 4: Implement pointer-last publication**

Stage at `.generations/<name>/.<guid>.tmp`, clone into `repository`, run contributors into `supplements`, rename the completed generation, then atomically replace state. Do not remove the current generation before state replacement. Best-effort prune noncurrent generations after publication. Preserve resumable Git staging separately. On startup or the next sync, remove abandoned temporary generations and completed generations not named by current state, never the current pointer target.

Change readers to:

```csharp
public Task<T> ReadCurrentSourceAsync<T>(
    string name, Func<SourceSnapshot, T> reader, CancellationToken cancellationToken);
```

Update Docs and API readers to use `snapshot.RepositoryDirectory`.

- [ ] **Step 5: Run the full existing suite**

Run: `dotnet test DotNetKnowledge.slnx --nologo`

Expected: PASS with source status returning the current generation repository path.

- [ ] **Step 6: Commit**

```powershell
git add src/DotNetKnowledge.Mcp/Sources src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsQueryService.cs tests/DotNetKnowledge.Mcp.Tests/Sources tests/DotNetKnowledge.Mcp.Tests/Features/Sources/SourcesToolTests.cs
git commit -m "refactor: publish synchronized sources as generations"
```

---

### Task 3: Download and validate NuGet archives

**Files:**
- Create: `src/DotNetKnowledge.Mcp/Sources/NuGetPackageClient.cs`
- Create: `src/DotNetKnowledge.Mcp/Sources/PackageArchiveReader.cs`
- Create: `tests/DotNetKnowledge.Mcp.Tests/Sources/NuGetPackageClientTests.cs`
- Create: `tests/DotNetKnowledge.Mcp.Tests/Sources/PackageArchiveReaderTests.cs`
- Modify: `src/DotNetKnowledge.Mcp/Program.cs`

**Interfaces:**
- Produces: `INuGetPackageClient.DownloadAsync` and `PackageArchiveReader.ReadAssets` for Task 6.

- [ ] **Step 1: Write failing NuGet v3 tests**

Use a fake `HttpMessageHandler` serving one `PackageBaseAddress/3.0.0`, `.nupkg.sha512`, and package body. Assert lowercase ID/version URLs, fixed-time hash comparison, cancellation, and no destination file after failure.

```csharp
var result = await client.DownloadAsync(
    package, "5.3.0", expectedSha512, destination, CancellationToken.None);
Assert.AreEqual(expectedSha512, result.Sha512);
CollectionAssert.AreEqual(packageBytes, await File.ReadAllBytesAsync(destination));
```

- [ ] **Step 2: Write failing archive tests**

Cover rooted paths, both slash forms of parent traversal, case-insensitive duplicate normalized paths, entries over 32 MiB, total content over 128 MiB, missing/duplicate pairs, and wrong basename. The success fixture contains repository-authored DLL/XML bytes under `lib/net8.0` and `lib/net10.0`.

- [ ] **Step 3: Verify the tests fail**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter "FullyQualifiedName~NuGetPackageClientTests|FullyQualifiedName~PackageArchiveReaderTests" --nologo`

Expected: compilation fails because both boundaries are absent.

- [ ] **Step 4: Implement the HTTP boundary**

```csharp
public sealed record NuGetPackageDownload(string Sha512, DateTimeOffset FetchedAt);
public interface INuGetPackageClient
{
    Task<NuGetPackageDownload> DownloadAsync(
        ApiPackageDefinition package, string version, string? expectedSha512,
        string destination, CancellationToken cancellationToken);
}
```

Require one usable HTTPS package-base resource, compare the server and optional catalog hashes as decoded 64-byte values, stream through incremental SHA-512 into a temporary file, and move only after success. Register singleton `HttpClient` and client.
Disable automatic redirects; follow at most five redirects manually and require every destination to remain HTTPS.

- [ ] **Step 5: Implement safe asset discovery**

```csharp
public sealed record PackageFrameworkAsset(string Framework, string AssemblyEntry, string XmlEntry);
public static IReadOnlyList<PackageFrameworkAsset> ReadAssets(
    string nupkgPath, ApiPackageDefinition definition);
```

Normalize separators, reject unsafe paths before lookup, enforce size ceilings while reading, and accept only paired `lib/<framework>/<AssemblyName>.dll` and `.xml`. Return frameworks ordinally. Open accepted entry streams directly; never extract the archive tree.

- [ ] **Step 6: Pass tests and commit**

Run the Step 3 command; expected: PASS.

```powershell
git add src/DotNetKnowledge.Mcp/Sources/NuGetPackageClient.cs src/DotNetKnowledge.Mcp/Sources/PackageArchiveReader.cs src/DotNetKnowledge.Mcp/Program.cs tests/DotNetKnowledge.Mcp.Tests/Sources/NuGetPackageClientTests.cs tests/DotNetKnowledge.Mcp.Tests/Sources/PackageArchiveReaderTests.cs
git commit -m "feat: verify and validate NuGet package downloads"
```

---

### Task 4: Decode visible metadata and C# signatures

**Files:**
- Create: `src/DotNetKnowledge.Mcp/Features/ApiDocs/Corpus/ApiCorpusModels.cs`
- Create: `src/DotNetKnowledge.Mcp/Features/ApiDocs/Corpus/MetadataApiReader.cs`
- Create: `tests/DotNetKnowledge.Mcp.Tests.ApiFixture/DotNetKnowledge.Mcp.Tests.ApiFixture.csproj`
- Create: `tests/DotNetKnowledge.Mcp.Tests.ApiFixture/FixtureApis.cs`
- Create: `tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/MetadataApiReaderTests.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj`
- Modify: `DotNetKnowledge.slnx`

**Interfaces:**
- Produces: `ApiCorpus` records and `MetadataApiReader.Read(Stream)` keyed by ECMA documentation ID.

- [ ] **Step 1: Add the fixture assembly and failing tests**

Create a `net10.0` fixture project with `GenerateDocumentationFile=true`. Declare a nested generic type; nullable tuple; arrays and pointers; every accessibility; constructor; constrained generic method; property, indexer, event, and field; operators; and `ref`/`in`/`out`. Reference it from the test project with `ReferenceOutputAssembly="false"` and expose its DLL/XML paths through assembly metadata, following `GitRunnerHostPath`.

```csharp
var corpus = MetadataApiReader.Read(File.OpenRead(FixtureAssemblyPath));
var type = corpus.Types.Single(item => item.FullName == "Fixtures.SignatureGallery<T>");
Assert.IsTrue(type.Members.Any(item =>
    item.Signature == "public ref readonly (string Name, T Value)? Borrow(in T value);"));
Assert.IsFalse(type.Members.Any(item => item.Name == "InternalOnly"));
Assert.IsFalse(type.Members.Any(item => item.Name == "PrivateProtectedOnly"));
```

Assert stable ECMA IDs and structural uses for base, interface, constraint, parameter, return, and attribute positions.

- [ ] **Step 2: Verify the tests fail**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter FullyQualifiedName~MetadataApiReaderTests --nologo`

Expected: compilation fails because the corpus reader is absent.

- [ ] **Step 3: Define the normalized schema**

```csharp
public sealed record ApiCorpus(int SchemaVersion, IReadOnlyList<ApiCorpusType> Types);
public sealed record ApiCorpusType(
    string EcmaId, string Name, string FullName, ApiTypeUse? BaseType,
    IReadOnlyList<ApiTypeUse> Interfaces, IReadOnlyList<ApiTypeUse> Constraints,
    IReadOnlyList<ApiAttributeUse> Attributes, ApiDocumentation Documentation,
    IReadOnlyList<ApiCorpusMember> Members);
public sealed record ApiCorpusMember(
    string EcmaId, string Name, string Kind, string Signature,
    IReadOnlyList<ApiTypeUse> Parameters, ApiTypeUse? ReturnType,
    IReadOnlyList<ApiTypeUse> Constraints, IReadOnlyList<ApiAttributeUse> Attributes,
    ApiDocumentation Documentation);
public sealed record ApiTypeUse(
    string? Name, string TypeExpression, IReadOnlyList<string> TypeNames);
public sealed record ApiDocumentation(
    string? Summary, IReadOnlyList<ApiNamedDocumentation> Parameters,
    IReadOnlyList<ApiNamedDocumentation> TypeParameters, string? Returns,
    string? Value, string? Remarks, IReadOnlyList<ApiNamedDocumentation> Exceptions);
```

`ApiTypeUse` stores display expression and canonical contained type names. `ApiAttributeUse` stores application text, CLR attribute name, and canonical argument type names.

- [ ] **Step 4: Implement metadata-only decoding**

Use `PEReader`, `MetadataReader`, and `ISignatureTypeProvider<DecodedType, SignatureContext>`. Walk containing-type visibility; include public, protected, and protected-internal; exclude internal, private, and private-protected. Decode nullable and tuple attributes without resolving dependencies. Generate ECMA identity independently from C# presentation. Throw `InvalidDataException` for unsupported signature elements rather than approximating them.

- [ ] **Step 5: Pass tests and commit**

Run the Step 2 command; expected: PASS.

```powershell
git add DotNetKnowledge.slnx src/DotNetKnowledge.Mcp/Features/ApiDocs/Corpus tests/DotNetKnowledge.Mcp.Tests.ApiFixture tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/MetadataApiReaderTests.cs
git commit -m "feat: decode package API metadata into a normalized corpus"
```

---

### Task 5: Join compiler XML and persist deterministic corpora

**Files:**
- Create: `src/DotNetKnowledge.Mcp/Features/ApiDocs/Corpus/PackageXmlDocsReader.cs`
- Create: `src/DotNetKnowledge.Mcp/Features/ApiDocs/Corpus/PackageApiCorpusBuilder.cs`
- Create: `src/DotNetKnowledge.Mcp/Features/ApiDocs/Corpus/PackageApiCorpusStore.cs`
- Create: `tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/PackageXmlDocsReaderTests.cs`
- Create: `tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/PackageApiCorpusBuilderTests.cs`

**Interfaces:**
- Produces: `PackageXmlDocsReader.Read(Stream)`, `PackageApiCorpusBuilder.BuildAsync` returning `PackageCorpusBuildResult`, and `PackageApiCorpusStore.Read`.

- [ ] **Step 1: Write failing XML reader tests**

Use compiler-style `<doc><members><member name="M:Fixtures.Type.Run(System.String)">` input. Assert summary, named params/typeparams, returns, value, remarks, and exceptions. Assert the existing rendering conventions for `see`, `seealso`, `paramref`, `typeparamref`, and `langword`. Duplicate IDs and malformed roots must fail.

- [ ] **Step 2: Write failing join and determinism tests**

```csharp
var state = await builder.BuildAsync(packagePath, definition, output, CancellationToken.None);
CollectionAssert.AreEqual(new[] { "net10.0", "net8.0" }, state.AvailableFrameworks);
Assert.IsTrue(File.Exists(Path.Combine(output, "net10.0.json")));
Assert.AreEqual(firstHash, Hash(Path.Combine(output, "net10.0.json")));
```

Build twice and require byte-identical JSON. Assert a visible undocumented member survives with empty docs and an XML-only nonvisible declaration is discarded.

- [ ] **Step 3: Verify the tests fail**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter "FullyQualifiedName~PackageXmlDocsReaderTests|FullyQualifiedName~PackageApiCorpusBuilderTests" --nologo`

Expected: compilation fails because the parser/builder/store are absent.

- [ ] **Step 4: Implement secure XML joining**

Parse with `DtdProcessing.Prohibit` and `XmlResolver = null`. Index by exact ordinal ECMA ID and reject duplicates. Extract the current reference renderer from `ApiDocsQueryService` into an internal helper used by both formats. Join docs onto metadata; never create an API from XML alone. Require the PE metadata assembly name to equal `ApiPackageDefinition.AssemblyName`.

- [ ] **Step 5: Implement deterministic storage**

Define `PackageCorpusBuildResult(IReadOnlyList<string> AvailableFrameworks, IReadOnlyDictionary<string, string> CorpusFiles)`. Sort types by `FullName`, members by `EcmaId`, and all set-like lists ordinally. Write compact schema-version-2 JSON through a temporary file. Validate package ID/version/hash/framework and the complete normalized graph on read, reject schema mismatch with a resynchronization-required error, and cache by those four identity values.

- [ ] **Step 6: Pass tests and commit**

Run the Step 3 command; expected: PASS.

```powershell
git add src/DotNetKnowledge.Mcp/Features/ApiDocs tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/PackageXmlDocsReaderTests.cs tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/PackageApiCorpusBuilderTests.cs
git commit -m "feat: normalize package XML and metadata by framework"
```

---

### Task 6: Build the supplement during Roslyn synchronization

**Files:**
- Create: `src/DotNetKnowledge.Mcp/Sources/RoslynPackageCohort.cs`
- Create: `src/DotNetKnowledge.Mcp/Sources/ApiPackageGenerationContributor.cs`
- Modify: `src/DotNetKnowledge.Mcp/Sources/ApiPackageDefinition.cs`
- Modify: `src/DotNetKnowledge.Mcp/Sources/SourceSynchronizer.cs`
- Modify: `src/DotNetKnowledge.Mcp/Program.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/Sources/SourcesTool.cs`
- Create: `tests/DotNetKnowledge.Mcp.Tests/Sources/RoslynPackageCohortTests.cs`
- Create: `tests/DotNetKnowledge.Mcp.Tests/Sources/ApiPackageGenerationContributorTests.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Sources/SourceSynchronizerTests.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/Sources/SourcesToolTests.cs`

**Interfaces:**
- Populates: Task 2's `ApiPackageSyncState` and implements `ISourceGenerationContributor`.

- [ ] **Step 1: Write failing cohort tests**

Create `roslyn-dotnet-5.2.0.json` and `roslyn-dotnet-5.3.0.json`; assert numeric maximum 5.3.0, internal version agreement, pinned catalog equality, and rejection of missing, mixed, or malformed manifests.

- [ ] **Step 2: Write failing composite-sync tests**

Use a fake package client and fixture archive. Assert stages `package-download`, `package-validate`, and `package-normalize` follow Git validation; state includes every framework/default; and hash mismatch, missing default, build failure, and cancellation retain the old generation.

```csharp
var result = await synchronizer.SyncAsync("roslyn-api-docs", null, CancellationToken.None);
Assert.HasCount(1, result.ApiPackages);
var package = result.ApiPackages[0];
Assert.AreEqual("5.3.0", package.Version);
Assert.AreEqual("net10.0", package.DefaultFramework);
CollectionAssert.Contains(package.AvailableFrameworks.ToList(), "net472");
```

- [ ] **Step 3: Verify the tests fail**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter "FullyQualifiedName~RoslynPackageCohortTests|FullyQualifiedName~ApiPackageGenerationContributorTests|FullyQualifiedName~SourceSynchronizerTests" --nologo`

Expected: compilation fails because package state/contributor are absent.

- [ ] **Step 4: Implement synchronized package state population**

```csharp
var state = new ApiPackageSyncState(
    definition.PackageId, definition.AssemblyName, cohortVersion, download.Sha512,
    definition.Feed, download.FetchedAt, definition.DefaultFramework,
    builtFrameworks, relativeCorpusDirectory);
```

Pinned mode requires the manifest cohort to equal the catalog version and verifies the catalog hash. Head mode derives version from the checked-out manifest, passes no catalog hash, verifies against NuGet's server hash, and records the observed value.

- [ ] **Step 5: Implement and register the contributor**

Download into staging, validate assets, build each corpus, require the case-insensitive default among available frameworks, delete the archive, and return a state with a supplement-root-relative corpus path. Register it in DI. Sources without packages remain Git-only.
Expose `SourceSynchronizer.GetStageCount(name)` as five Git stages plus three package stages when a supplement applies, and use it in `SourcesTool.StageReporter`; extend progress tests to assert the composite denominator and order.

- [ ] **Step 6: Pass tests and commit**

Run the Step 3 command and `dotnet test DotNetKnowledge.slnx --nologo`; expected: PASS.

```powershell
git add src/DotNetKnowledge.Mcp/Sources src/DotNetKnowledge.Mcp/Program.cs src/DotNetKnowledge.Mcp/Features/Sources/SourcesTool.cs tests/DotNetKnowledge.Mcp.Tests/Sources tests/DotNetKnowledge.Mcp.Tests/Features/Sources/SourcesToolTests.cs
git commit -m "feat: synchronize Roslyn API package supplements atomically"
```

---

### Task 7: Expose composite status and discriminated provenance

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsModels.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsQueryService.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiSearchRanking.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiTextRanking.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocsModels.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/Sources/SourcesTool.cs`
- Modify: `src/DotNetKnowledge.Mcp/Sources/SourceSyncResult.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/Sources/SourcesToolTests.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiSearchRankingTests.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiTextRankingTests.cs`

**Interfaces:**
- Replaces API `SourceProvenance` with `ApiProvenance`, `GitProvenance`, and `NuGetProvenance`.
- Produces package supplement status for Tasks 8-10.

- [ ] **Step 1: Write failing wire tests**

Assert Git JSON adds `kind: "git"` without losing fields; NuGet JSON contains kind/package/version/hash/feed/framework/fetchedAt and no repo/commit. Assert list/sync status reports package identity, sync state, frameworks, default, and verified hash.

- [ ] **Step 2: Verify the tests fail**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter "FullyQualifiedName~SourcesToolTests|FullyQualifiedName~DocsQueryServiceTests|FullyQualifiedName~ApiSearchRankingTests|FullyQualifiedName~ApiTextRankingTests" --nologo`

Expected: provenance/status assertions fail.

- [ ] **Step 3: Add the provenance union**

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(GitProvenance), "git")]
[JsonDerivedType(typeof(NuGetProvenance), "nuget")]
public abstract record ApiProvenance
{
    [JsonIgnore]
    public abstract string RevisionKey { get; }
}
public sealed record GitProvenance(
    string Repo, string Ref, string Commit, DateTimeOffset FetchedAt) : ApiProvenance
{
    [JsonIgnore]
    public override string RevisionKey => JsonSerializer.Serialize(new { kind = "git", Repo, Ref, Commit });
}
public sealed record NuGetProvenance(
    string PackageId, string Version, string Sha512, string Feed,
    string Framework, DateTimeOffset FetchedAt) : ApiProvenance
{
    [JsonIgnore]
    public override string RevisionKey => JsonSerializer.Serialize(
        new { kind = "nuget", PackageId, Version, Sha512, Feed, Framework });
}
```

Build `RevisionKey` by serializing identity fields, not delimiter concatenation. Keep document tools typed to `GitProvenance`; API results use `ApiProvenance`.
Replace API ranking and ordering references to `Source.Repo` with `Source.RevisionKey`; verify existing Git-only ordering tests remain unchanged before adding package cases.

- [ ] **Step 4: Add supplement status**

Map definitions and synchronized state into `supplements` on list/sync responses. A missing configured supplement makes the composite source unsynchronized and retains the existing sync remedy.

- [ ] **Step 5: Pass tests and commit**

Run the Step 2 command; expected: PASS.

```powershell
git add src/DotNetKnowledge.Mcp/Features src/DotNetKnowledge.Mcp/Sources/SourceSyncResult.cs tests/DotNetKnowledge.Mcp.Tests/Features
git commit -m "feat: report package supplement status and provenance"
```

---

### Task 8: Introduce common repository and package backends

**Files:**
- Create: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsBackend.cs`
- Create: `src/DotNetKnowledge.Mcp/Features/ApiDocs/RepositoryApiDocsBackend.cs`
- Create: `src/DotNetKnowledge.Mcp/Features/ApiDocs/PackageApiDocsBackend.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsQueryService.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiDocsFixture.cs`
- Create: `tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/PackageApiDocsBackendTests.cs`

**Interfaces:**
- Produces: `IApiDocsBackend` operations and `ApiQueryCoverage` for Task 9.

- [ ] **Step 1: Write failing package-backend tests**

Build a snapshot with two corpus files. Assert exact type/member lookup, name classification, text labels/budget, six reference kinds, attribute sibling resolution, and NuGet provenance. Use existing ranking implementations.

- [ ] **Step 2: Verify the tests fail**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter FullyQualifiedName~PackageApiDocsBackendTests --nologo`

Expected: compilation fails because the backend contract is absent.

- [ ] **Step 3: Define the backend contract**

```csharp
internal interface IApiDocsBackend
{
    ApiLookupRead Lookup(string symbol, CancellationToken cancellationToken);
    ApiSearchRead Search(string pattern, CancellationToken cancellationToken);
    ApiTextRead SearchText(string query, CancellationToken cancellationToken);
    ApiReferenceRead FindReferences(string symbol, CancellationToken cancellationToken);
}
internal sealed record ApiQueryCoverage(
    IReadOnlyList<ApiProvenance> SearchedSources, string? EffectiveFramework,
    string? DefaultFramework, IReadOnlyList<string>? AvailableFrameworks);
```

Read records contain complete unpaged matches plus coverage. Pagination and cross-backend ranking stay in `ApiDocsQueryService`.

- [ ] **Step 4: Extract the repository backend unchanged**

Move docs-root resolution, XML lookup, name classification, text extraction, and reference extraction from the service. Preserve existing tests except fixture construction. Run current service tests before package merge work.

- [ ] **Step 5: Implement the package backend**

Map corpus records to the same reads. Match structural references from canonical contained type names while returning stored C# expressions. Resolve attribute sibling collisions within the selected corpus. Never reopen package XML or DLL files.

- [ ] **Step 6: Pass tests and commit**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter "FullyQualifiedName~ApiDocsQueryServiceTests|FullyQualifiedName~PackageApiDocsBackendTests" --nologo`

Expected: PASS.

```powershell
git add src/DotNetKnowledge.Mcp/Features/ApiDocs tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs
git commit -m "refactor: query API documentation through corpus backends"
```

---

### Task 9: Select frameworks, merge declarations, and bind cursors

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsModels.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsQueryService.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiDocsQueryServiceTests.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiDocsFixture.cs`

**Interfaces:**
- Changes: `LookupAsync(symbol, source, framework, limit, cursor, cancellationToken)`.
- Changes: `SearchAsync(pattern, framework, limit, cursor, cancellationToken)`; name search keeps its existing all-source behavior and still has no `source` parameter.
- Changes: `SearchTextAsync(query, source, framework, limit, cursor, cancellationToken)`.
- Changes: `FindReferencesAsync(symbol, kind, exact, source, framework, limit, cursor, cancellationToken)`.
- Produces: `FrameworkNotAvailableException` and coverage fields for Task 10.

- [ ] **Step 1: Write failing framework tests for all operations**

Assert omission selects `net10.0`, case-insensitive `NET8.0` returns canonical `net8.0`, and `net7.0` throws:

```csharp
var error = await Assert.ThrowsExactlyAsync<FrameworkNotAvailableException>(
    () => service.LookupAsync("MSBuildWorkspace", "roslyn-api-docs", "net7.0",
        20, null, CancellationToken.None));
CollectionAssert.AreEqual(
    new[] { "net10.0", "net472", "net8.0", "net9.0" }, error.AvailableFrameworks);
Assert.AreEqual("net10.0", error.DefaultFramework);
```

For the three operations that accept `source`, any framework with `source: "dotnet-api-docs"` must throw `ArgumentException` named `framework`. `search_api` always applies its framework to the Roslyn supplement while querying framework-neutral Git sources normally.

- [ ] **Step 2: Write failing merge and cursor tests**

Assert Git replaces package declarations by canonical ECMA ID; package-only members survive; duplicate text/reference hits consume one slot and one total; ordering is stable; and changing only framework, package hash/version, or Git commit invalidates a cursor.

- [ ] **Step 3: Verify the tests fail**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter FullyQualifiedName~ApiDocsQueryServiceTests --nologo`

Expected: new framework/merge assertions fail.

- [ ] **Step 4: Implement selection and result coverage**

Add `EffectiveFramework`, `DefaultFramework`, and `AvailableFrameworks` to all four results. Resolve a framework only when Roslyn participates; compare case-insensitively and return canonical package spelling.

```csharp
public sealed class FrameworkNotAvailableException : ArgumentException
{
    public string RequestedFramework { get; }
    public string DefaultFramework { get; }
    public IReadOnlyList<string> AvailableFrameworks { get; }
}
```

- [ ] **Step 5: Implement merge and cursor identity**

Merge lookup by type/member ECMA ID, name search by canonical type, text by declaration/element/normalized text, and references by declaration/kind/parameter/type expression. Add Git records first so they win. Compute totals after deduplication and before filtering/paging. Include canonical framework in scope and every provenance `RevisionKey` in cursor revisions.

- [ ] **Step 6: Carry honest empty-result coverage**

Preserve coverage on type- and member-not-found outcomes. Remove the assumption that another name search proves absence; Task 10 formats the wire message.

- [ ] **Step 7: Pass tests and commit**

Run the Step 3 command; expected: PASS.

```powershell
git add src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsModels.cs src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsQueryService.cs tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiDocsQueryServiceTests.cs tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiDocsFixture.cs
git commit -m "feat: select and merge framework-specific API package data"
```

---

### Task 10: Expose framework and failures over MCP

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsTool.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiDocsToolTests.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Protocol/McpStdioTests.cs`

**Interfaces:**
- Adds optional `framework` to all four tools.
- Produces: `framework_not_available` and coverage-aware `not_found` envelopes.

- [ ] **Step 1: Write failing serialization tests**

For every tool, assert `framework: "net8.0"` returns `effectiveFramework`. Assert unknown framework returns:

```json
{
  "error": "framework_not_available",
  "requestedFramework": "net7.0",
  "defaultFramework": "net10.0",
  "availableFrameworks": ["net10.0", "net472", "net8.0", "net9.0"]
}
```

Assert `not_found` includes exact Git/package/framework coverage and omits `Call search_api`; retain the member-specific lookup remedy.

- [ ] **Step 2: Write failing protocol schema tests**

Inspect all four input schemas and assert `framework` exists but is not required. Preserve existing optional parameters.

- [ ] **Step 3: Verify the tests fail**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter "FullyQualifiedName~ApiDocsToolTests|FullyQualifiedName~McpStdioTests" --nologo`

Expected: wire/schema assertions fail.

- [ ] **Step 4: Add parameters and error mapping**

Add `framework` after `source` on `lookup_api`, `search_api_text`, and `find_api_references`; add it before `limit` on `search_api`, which has no source argument. Pass it to the exact Task 9 service signatures. Catch `FrameworkNotAvailableException` before `ArgumentException`; framework with dotnet source remains `invalid_request`.

- [ ] **Step 5: Format honest absence**

Serialize coverage fields and use: `API symbol '<symbol>' was not found in the stated synchronized coverage; the name may be invalid or its package may be outside that coverage.` Do not claim corpus completeness.

- [ ] **Step 6: Pass tests and commit**

Run the Step 3 command; expected: PASS.

```powershell
git add src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsTool.cs tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiDocsToolTests.cs tests/DotNetKnowledge.Mcp.Tests/Protocol/McpStdioTests.cs
git commit -m "feat: expose framework-specific API queries over MCP"
```

---

### Task 11: Update current-truth documentation and close the backlog

**Files:**
- Modify: `README.md`
- Modify: `CLAUDE.md`
- Modify: `docs/design/mcp-tool-surface.md`
- Modify: `docs/decisions.md`
- Modify: `docs/backlog/README.md`
- Delete: `docs/backlog/api-coverage-stops-at-the-documented-package-set.md`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Sources/SourceCatalogTests.cs`

**Interfaces:**
- Documents composite sync, framework selection, provenance, merge behavior, and absence semantics.

- [ ] **Step 1: Preserve the backlog item in history if it is still uncommitted**

Run `git log --all --oneline -- docs/backlog/api-coverage-stops-at-the-documented-package-set.md`. If it returns no commit and the file/index row are still the pre-existing user changes recorded when this plan was written, commit only those two paths before deleting them later:

```powershell
git add docs/backlog/README.md docs/backlog/api-coverage-stops-at-the-documented-package-set.md
git commit -m "docs: record uncovered Roslyn API package gap"
```

If history already contains the file, make no baseline commit.

- [ ] **Step 2: Write a failing documentation contract**

Extend `SourceCatalogTests` to assert the package declaration and that the backlog index no longer contains `api-coverage-stops-at-the-documented-package-set.md`.

- [ ] **Step 3: Verify the test fails**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter FullyQualifiedName~SourceCatalogTests --nologo`

Expected: the backlog assertion fails.

- [ ] **Step 4: Update current truth**

Document `framework?`, the four TFMs and default, composite sync, repository-wins merging, structured framework failure, coverage-aware absence, and Git/NuGet provenance in the README, CLAUDE, and tool-surface documents.

- [ ] **Step 5: Append decisions**

Append dated entries for server-owned pinned packages; XML-plus-metadata normalization; all-framework selection with explicit default; and discriminated provenance with Git precedence. Never edit earlier entries.

- [ ] **Step 6: Delete the resolved backlog item**

Delete the file and only its index row. Git history remains the record.

- [ ] **Step 7: Verify and commit**

```powershell
dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter FullyQualifiedName~SourceCatalogTests --nologo
dotnet scripts/verify-no-vendored-content.cs
git diff --check
```

Expected: all exit 0.

```powershell
git add README.md CLAUDE.md docs/design/mcp-tool-surface.md docs/decisions.md docs/backlog tests/DotNetKnowledge.Mcp.Tests/Sources/SourceCatalogTests.cs
git commit -m "docs: describe pinned package API coverage"
```

---

### Task 12: Probe the real package and verify the repository

**Files:**
- Create: `scripts/probes/probe-api-package-supplement.cs`
- Modify: `scripts/probes/README.md`

**Interfaces:**
- Produces an optional operator-invoked compatibility diagnostic; automated behavior remains covered offline.

- [ ] **Step 1: Add the real-package probe**

Create a single-file C# tool accepting `--package <path>` after `--`. It invokes the production archive validator and corpus builder, then prints JSON with package ID, SHA-512, frameworks/default, `MSBuildWorkspace` member count, and its four `Create` overload signatures. The README must say this proves compatibility with one local copy, not download behavior or future layouts.

- [ ] **Step 2: Run the probe**

```powershell
dotnet scripts/probes/probe-api-package-supplement.cs -- --package "$env:USERPROFILE\.nuget\packages\microsoft.codeanalysis.workspaces.msbuild\5.3.0\microsoft.codeanalysis.workspaces.msbuild.5.3.0.nupkg"
```

Expected: exit 0; exact four TFMs; pinned hash; `MSBuildWorkspace`; and four `Create` overloads.

- [ ] **Step 3: Run full verification**

```powershell
dotnet test DotNetKnowledge.slnx --configuration Release --nologo
dotnet scripts/verify-no-vendored-content.cs
git diff --check
git status --short
```

Expected: tests pass, guards exit 0, and only intended implementation or pre-existing user-owned changes are shown.

- [ ] **Step 4: Commit the probe**

```powershell
git add scripts/probes/probe-api-package-supplement.cs scripts/probes/README.md
git commit -m "test: add pinned API package compatibility probe"
```

- [ ] **Step 5: Re-run Step 3 against committed state**

Expected: all commands exit 0 and status shows only pre-existing user-owned changes, if any.
