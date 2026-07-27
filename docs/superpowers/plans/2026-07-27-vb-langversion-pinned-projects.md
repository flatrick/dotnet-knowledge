# VB.NET per-`LangVersion` pinned projects — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the VB.NET corpus the same per-`LangVersion` proof the C# corpus has — a project per language-version pin per family, each building at 0 errors and 0 warnings.

**Architecture:** Each VB family gets one source tree under `src/` and a ladder of pinned projects under `<pin>/<kind>/` that select rows with explicit `Compile` globs. VB prepends `RootNamespace` to every declaration, so one copy of each sample serves every pin — unlike C#, which must duplicate sources physically. `scripts/verify-feature-floors.cs` grows a VB ladder so a row's version claim is probed rather than asserted.

**Tech Stack:** .NET SDK 10, MSBuild, MSTest, single-file C# scripts (`dotnet scripts/foo.cs -- <args>`), Roslyn `vbc`, `Microsoft.Net.Compilers` 1.3.2, in-box `%WINDIR%\Microsoft.NET\Framework64` compilers.

**Spec:** [`docs/superpowers/specs/2026-07-27-vb-langversion-pinned-projects-design.md`](../specs/2026-07-27-vb-langversion-pinned-projects-design.md)

## Global Constraints

- **Every project build requires 0 errors AND 0 warnings.** `TreatWarningsAsErrors` is inherited from the repository root `Directory.Build.props`. Never add `#pragma warning disable` or a VB `#Disable Warning` to get past a warning, and never override `TreatWarningsAsErrors` in the corpus subtree.
- **Tooling is single-file C#, never a shell script.** No `.sh`, `.ps1`, `.bat`, `.py`. Arguments go after `--`.
- **LF line endings, UTF-8**, enforced by `.gitattributes`.
- **American English** in identifiers, comments, and prose.
- **State current truth only.** No "previously said X" footers, no dated verification stamps. Do not add fixed counts to documentation — a count is stale the moment a row or project is added.
- **Never commit upstream content.** Run `dotnet scripts/verify-no-vendored-content.cs` before committing anything that adds files in bulk.
- **Do not run `scripts/generate-net48-examples.cs`.** It targets deleted project roots.
- **Do not touch the C# corpus tree**, except for adding one `PackageReference` to `CSharp_v8.0` in Task 7.
- **The VB language-version ladder is exactly** `9, 10, 11, 12, 14, 15, 15.3, 15.5, 16, 16.9, 17.13, latest`. `vbc` rejects `17` and `17.0` with `BC2014`. Note the bare `16`, not `16.0`.
- **Corpus version folder names map to ladder values** by replacing `_` with `.` and stripping the `Vb` prefix: `Vb15_3` → `15.3`, `Vb16_0` → `16`, `Vb17_13` → `17.13`. `Baseline` has no single version.

---

## File Structure

**Created:**

- `examples/language-features/VB.NET/dotnet/Net10/Directory.Build.props` — net10 family baggage.
- `examples/language-features/VB.NET/dotnet/Net10/src/` — the family's only copy of its row sources.
- `examples/language-features/VB.NET/dotnet/Net10/<pin>/library/library.vbproj` + `.slnx` — one per pin.
- `examples/language-features/VB.NET/dotNetFramework/v4.8/Directory.Build.props` — net48 family baggage.
- `examples/language-features/VB.NET/dotNetFramework/v4.8/src/` — likewise.
- `examples/language-features/VB.NET/dotNetFramework/v4.8/<pin>/library/library.vbproj` + `.slnx`.
- `examples/language-features/VB.NET/dotNetFramework/v4.8/<pin>/my/my.vbproj` + `.slnx` — pins `11` and `latest` only.
- `tests/DotNetKnowledge.Corpus.Tests/Projects/VbSourceCoverageTests.cs` — orphan-row guard.

**Modified:**

- `tests/DotNetKnowledge.Corpus.Tests/Projects/CorpusProjectDiscovery.cs` — VB scan.
- `tests/DotNetKnowledge.Corpus.Tests/Projects/CorpusProjectDiscoveryTests.cs` — expected VB list.
- `tests/DotNetKnowledge.Corpus.Tests/CorpusProjectBuildTests.cs` — build VB projects too.
- `scripts/verify-feature-floors.cs` — language profile, VB ladder, VB escalation, locale-proof diagnostics.
- `scripts/verify-project-namespaces.cs` — inverse VB rule.
- `examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v8.0/CSharp80.csproj` — reference assemblies.
- `examples/language-features/MANIFEST.md`, `AGENTS.md`, `CLAUDE.md`, `docs/HANDOFF.md`, `docs/design/language-feature-showcase-design.md`.

**Deleted:** the two old family-root project files and the row folders at the family roots (moved into `src/`).

---

### Task 1: Repair the two forward-reaching `Baseline` samples

Two `Baseline` samples use readonly auto-implemented properties, a VB 14 feature, inside rows filed in the VS.NET 2002–VS2012 bucket. They must compile at VB 11 before any pin-11 project can exist.

**Files:**
- Modify: `examples/language-features/VB.NET/dotnet/Net10/Baseline/Attributes/Attributes.vb`
- Modify: `examples/language-features/VB.NET/dotnet/Net10/Baseline/AutoImplementedPropertiesAndCollectionInitializers/AutoImplementedPropertiesAndCollectionInitializers.vb`
- Modify: `examples/language-features/VB.NET/dotNetFramework/v4.8/Baseline/Attributes/Attributes.vb`
- Modify: `examples/language-features/VB.NET/dotNetFramework/v4.8/Baseline/AutoImplementedPropertiesAndCollectionInitializers/AutoImplementedPropertiesAndCollectionInitializers.vb`

**Interfaces:**
- Consumes: nothing.
- Produces: a `Baseline` tree that compiles at `LangVersion=11` in both families. Tasks 2 and 7 depend on this.

The two families' copies of these files are byte-identical, so apply the identical edit to both.

- [ ] **Step 1: Write the failing probe**

Create a scratch project outside the repository and point it at the net10 `Baseline` tree. Run from the repository root:

```bash
S="$TEMP/vb-floor-probe" && rm -rf "$S" && mkdir -p "$S"
cp -r examples/language-features/VB.NET/dotnet/Net10/Baseline "$S/Baseline"
cat > "$S/p.vbproj" <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Net10_Vb11_Library</RootNamespace>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>11</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
XML
```

- [ ] **Step 2: Run the probe to verify it fails**

Run: `dotnet build "$S/p.vbproj" -nologo`

Expected: FAIL, with two occurrences of
`error BC36716: Visual Basic 11.0 does not support readonly auto-implemented properties.`
naming `Attributes.vb` and `AutoImplementedPropertiesAndCollectionInitializers.vb`.

- [ ] **Step 3: Repair `Attributes.vb` in both families**

The `Attributes` row demonstrates attributes; the read-only property is incidental. Replace this block:

```vb
        Public Sub New(reason As String)
            Me.Reason = reason
        End Sub

        Public ReadOnly Property Reason As String
```

with:

```vb
        Private ReadOnly _reason As String

        Public Sub New(reason As String)
            _reason = reason
        End Sub

        ' A ReadOnly property over an explicit backing field. A ReadOnly
        ' *auto*-implemented property is VB 14, above this row's era.
        Public ReadOnly Property Reason As String
            Get
                Return _reason
            End Get
        End Property
```

- [ ] **Step 4: Repair `AutoImplementedPropertiesAndCollectionInitializers.vb` in both families**

The row sits in the VS2010 bucket, and the comment describes a capability that arrived four years later. Replace this block:

```vb
        ' ...and a ReadOnly one may still be assigned from a constructor.
        Public ReadOnly Property Id As String

        Public Sub New(id As String)
            _Id = id
            Name = String.Empty
        End Sub
```

with:

```vb
        ' A read-only value assigned from the constructor, written the pre-VB14
        ' way: an explicit backing field behind a ReadOnly property. A ReadOnly
        ' *auto*-implemented property is VB 14, above this row's era.
        Private ReadOnly _id As String

        Public ReadOnly Property Id As String
            Get
                Return _id
            End Get
        End Property

        Public Sub New(id As String)
            _id = id
            Name = String.Empty
        End Sub
```

The three remaining auto-implemented properties (`Name`, `Age`, `Region`) are untouched, so the row still demonstrates what it is named for. `NameFieldLength` still reads the `_Name` backing field. Both `New Customer(...)` call sites keep their argument.

Do **not** add a new manifest row for readonly auto-implemented properties. The manifest's cited VB 14 source enumerates its rows and this is not among them; they are all already present.

- [ ] **Step 5: Re-run the probe against the repaired net10 tree**

```bash
rm -rf "$S/Baseline" && cp -r examples/language-features/VB.NET/dotnet/Net10/Baseline "$S/Baseline"
dotnet build "$S/p.vbproj" -nologo
```

Expected: PASS, `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 6: Repeat the probe against the repaired net48 tree**

`docs/HANDOFF.md` records that a whole-project VB build can stop early and under-report, so verify the second family rather than assuming it matches. Copy `examples/language-features/VB.NET/dotNetFramework/v4.8/Baseline` into the scratch directory, change the project's `TargetFramework` to `net48` and `RootNamespace` to `Net48_Vb11_Library`, add
`<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />`,
and temporarily exclude `Baseline/MyNamespaceHelpers` — that row needs `MyType=Windows`, which Task 7 gives it.

Expected: PASS, `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add examples/language-features/VB.NET/dotnet/Net10/Baseline examples/language-features/VB.NET/dotNetFramework/v4.8/Baseline
git commit -m "fix: keep two Baseline VB rows inside their own era

Attributes and AutoImplementedPropertiesAndCollectionInitializers both
used readonly auto-implemented properties, which are VB 14, inside rows
filed in the VS.NET 2002-VS2012 bucket. Both now use an explicit backing
field behind a ReadOnly property, which compiles at VB 11."
```

---

### Task 2: Restructure the net10 VB family into pinned projects

**Files:**
- Create: `examples/language-features/VB.NET/dotnet/Net10/Directory.Build.props`
- Move: `examples/language-features/VB.NET/dotnet/Net10/{Baseline,Vb14,Vb15,Vb15_3,Vb15_5,Vb16_0,Vb16_9,Vb17_13}` → `.../Net10/src/`
- Create: `examples/language-features/VB.NET/dotnet/Net10/<pin>/library/library.vbproj` and `library.slnx`, for each pin in `11, 14, 15, 15.3, 15.5, 16, 16.9, 17.13, latest`
- Delete: `examples/language-features/VB.NET/dotnet/Net10/VbNetNet10Latest.vbproj`, and the stale `bin/` and `obj/` at the family root

**Interfaces:**
- Consumes: Task 1's repaired `Baseline`.
- Produces: SDK-style VB library projects whose `RootNamespace` values are `Net10_Vb11_Library`, `Net10_Vb14_Library`, `Net10_Vb15_Library`, `Net10_Vb15_3_Library`, `Net10_Vb15_5_Library`, `Net10_Vb16_Library`, `Net10_Vb16_9_Library`, `Net10_Vb17_13_Library`, `Net10_VbLatest_Library`. Tasks 3, 5, 8 and 10 consume these paths and names.

This task uses the **at-or-below-pin** row set: a pin gets every version folder whose version is at or below it. Rows above a pin that `LangVersion` does not gate are added in Task 9, once the probe from Tasks 6–8 can say which they are. Splitting it this way keeps every step verifiable and avoids depending on the provisional table in the spec.

- [ ] **Step 1: Move the sources under `src/`**

```bash
cd examples/language-features/VB.NET/dotnet/Net10
mkdir src
git mv Baseline Vb14 Vb15 Vb15_3 Vb15_5 Vb16_0 Vb16_9 Vb17_13 src/
git rm VbNetNet10Latest.vbproj
rm -rf bin obj
```

- [ ] **Step 2: Create the family props**

`examples/language-features/VB.NET/dotnet/Net10/Directory.Build.props`:

```xml
<Project>
  <!--
    MSBuild's automatic discovery stops at the first Directory.Build.props found walking up from
    a project, which is this file. The corpus's zero-warning gate depends on TreatWarningsAsErrors
    arriving from the repository root, so the chain has to be continued explicitly.
  -->
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
  <PropertyGroup>
    <!-- Row sources live under src/ and are selected per pin, so the SDK's default globbing has
         nothing to contribute and is turned off rather than left to find an empty project cone. -->
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Create the pin-11 project**

`examples/language-features/VB.NET/dotnet/Net10/11/library/library.vbproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Net10_Vb11_Library</RootNamespace>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>11</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../src/Baseline/**/*.vb" LinkBase="Baseline" />
  </ItemGroup>
</Project>
```

`TargetFramework` stays in the project rather than moving to props: `CorpusProjectDiscovery` reads raw project XML instead of evaluating MSBuild, and throws when the element is absent.

`Baseline/` is pinned at 11, its era ceiling. It spans VS.NET 2002 to VS2012 and the upstream sources give no per-version attribution below VB 14, so rungs 9, 10 and 12 get no project rather than inventing attribution.

- [ ] **Step 4: Create the remaining eight projects**

Each is the same shape. `RootNamespace`, `LangVersion`, and the `Compile` items vary:

| Directory | `RootNamespace` | `LangVersion` | `Compile` items (in addition to all rows of lower pins) |
|---|---|---|---|
| `11/library` | `Net10_Vb11_Library` | `11` | `src/Baseline` |
| `14/library` | `Net10_Vb14_Library` | `14` | `src/Vb14` |
| `15/library` | `Net10_Vb15_Library` | `15` | `src/Vb15` |
| `15.3/library` | `Net10_Vb15_3_Library` | `15.3` | `src/Vb15_3` |
| `15.5/library` | `Net10_Vb15_5_Library` | `15.5` | `src/Vb15_5` |
| `16/library` | `Net10_Vb16_Library` | `16` | `src/Vb16_0` |
| `16.9/library` | `Net10_Vb16_9_Library` | `16.9` | `src/Vb16_9` |
| `17.13/library` | `Net10_Vb17_13_Library` | `17.13` | `src/Vb17_13` |
| `latest/library` | `Net10_VbLatest_Library` | `latest` | (same set as `17.13`) |

Each project lists every folder cumulatively. For example `15.3/library/library.vbproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Net10_Vb15_3_Library</RootNamespace>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>15.3</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../src/Baseline/**/*.vb" LinkBase="Baseline" />
    <Compile Include="../../src/Vb14/**/*.vb" LinkBase="Vb14" />
    <Compile Include="../../src/Vb15/**/*.vb" LinkBase="Vb15" />
    <Compile Include="../../src/Vb15_3/**/*.vb" LinkBase="Vb15_3" />
  </ItemGroup>
</Project>
```

Relative `Compile` paths resolve against the project directory, which is correct here. The `$(MSBuildThisFileDirectory)` anchor is only needed inside a props file, where items are evaluated in the consuming project's context.

`latest/library` keeps `RootNamespace` `Net10_VbLatest_Library`, unchanged from the project it replaces, so the `VbLatest` label in `MANIFEST.md` keeps resolving.

- [ ] **Step 5: Create a `.slnx` beside each project**

Every corpus project has one. `examples/language-features/VB.NET/dotnet/Net10/15.3/library/library.slnx`:

```xml
<Solution>
  <Project Path="library.vbproj" />
</Solution>
```

- [ ] **Step 6: Build every pin and verify zero warnings**

```bash
for p in 11 14 15 15.3 15.5 16 16.9 17.13 latest; do
  echo "== $p"
  dotnet build "examples/language-features/VB.NET/dotnet/Net10/$p/library/library.vbproj" -t:Rebuild --nologo -v:minimal
done
```

Expected: every pin reports `0 Warning(s)` and `0 Error(s)`.

If a pin fails with `BC36716`, a row reaches past its era — that is a real finding. Do not raise the pin to make it pass; report it and stop.

- [ ] **Step 7: Commit**

```bash
git add -A examples/language-features/VB.NET/dotnet/Net10
git commit -m "feat: pin the net10 VB corpus to one project per language version

Sources move to src/ and each pin selects its rows with explicit Compile
globs. VB prepends RootNamespace, so one copy of each sample serves every
pin and the copies cannot drift."
```

---

### Task 3: Discover and build VB projects in the test suite

Until this lands, "proven at this language version" rests on a manual build. This task makes it mechanical.

**Files:**
- Modify: `tests/DotNetKnowledge.Corpus.Tests/Projects/CorpusProjectDiscovery.cs`
- Modify: `tests/DotNetKnowledge.Corpus.Tests/Projects/CorpusProjectDiscoveryTests.cs`
- Modify: `tests/DotNetKnowledge.Corpus.Tests/CorpusProjectBuildTests.cs:63-76`

**Interfaces:**
- Consumes: Task 2's nine net10 project paths.
- Produces: `CorpusProjectDiscovery.FindSdkStyleVbProjects(string repositoryRoot)` and `CorpusProjectDiscovery.FindAllSdkStyleProjects(string repositoryRoot)`, both returning `IReadOnlyList<CorpusProject>`. Task 7's net48 projects are covered automatically once this exists.

`CorpusProject` is left unchanged. `CorpusProjectBuildTests` derives the language label from the file extension instead, which avoids rewriting every existing expectation.

- [ ] **Step 1: Write the failing test**

Add to `CorpusProjectDiscoveryTests`:

```csharp
[TestMethod]
public void FindSdkStyleVbProjectsReturnsTheCompleteSortedVbMatrix()
{
    var repositoryRoot = RepositoryRoot();
    CorpusProject[] expected =
    [
        new("examples/language-features/VB.NET/dotnet/Net10/11/library/library.vbproj", "net10.0", "11"),
        new("examples/language-features/VB.NET/dotnet/Net10/14/library/library.vbproj", "net10.0", "14"),
        new("examples/language-features/VB.NET/dotnet/Net10/15/library/library.vbproj", "net10.0", "15"),
        new("examples/language-features/VB.NET/dotnet/Net10/15.3/library/library.vbproj", "net10.0", "15.3"),
        new("examples/language-features/VB.NET/dotnet/Net10/15.5/library/library.vbproj", "net10.0", "15.5"),
        new("examples/language-features/VB.NET/dotnet/Net10/16.9/library/library.vbproj", "net10.0", "16.9"),
        new("examples/language-features/VB.NET/dotnet/Net10/16/library/library.vbproj", "net10.0", "16"),
        new("examples/language-features/VB.NET/dotnet/Net10/17.13/library/library.vbproj", "net10.0", "17.13"),
        new("examples/language-features/VB.NET/dotnet/Net10/latest/library/library.vbproj", "net10.0", "latest")
    ];

    var actual = CorpusProjectDiscovery.FindSdkStyleVbProjects(repositoryRoot).ToArray();

    CollectionAssert.AreEqual(expected, actual);
    Assert.IsFalse(actual.Any(project => project.RepositoryRelativePath.Contains("/bin/", StringComparison.Ordinal)));
    Assert.IsFalse(actual.Any(project => project.RepositoryRelativePath.Contains("/obj/", StringComparison.Ordinal)));
}
```

The expected order is ordinal by path, which is why `16.9` sorts before `16` — `.` (U+002E) precedes `/` (U+002F). Keep the list in that order rather than "fixing" it.

- [ ] **Step 2: Run the test to verify it fails**

Run the suite through the private host documented in [`scripts/install-corpus-test-sdks.md`](../../../scripts/install-corpus-test-sdks.md), filtering to `FindSdkStyleVbProjectsReturnsTheCompleteSortedVbMatrix`.

Expected: FAIL — `FindSdkStyleVbProjects` does not exist.

- [ ] **Step 3: Extract the shared scan and add the VB entry point**

In `CorpusProjectDiscovery.cs`, replace the body of `FindSdkStyleLibraries` with a call to a shared private helper, and add the two new public methods:

```csharp
public static IReadOnlyList<CorpusProject> FindSdkStyleLibraries(string repositoryRoot) =>
    Scan(repositoryRoot, ["CSharp", "dotnet"], "*.csproj", "SDK-style C# corpus");

// The VB net48 family is SDK-style, unlike most of the C# net48 tree, so both VB families are
// scanned from one root.
public static IReadOnlyList<CorpusProject> FindSdkStyleVbProjects(string repositoryRoot) =>
    Scan(repositoryRoot, ["VB.NET"], "*.vbproj", "VB.NET corpus");

public static IReadOnlyList<CorpusProject> FindAllSdkStyleProjects(string repositoryRoot) =>
    [.. FindSdkStyleLibraries(repositoryRoot), .. FindSdkStyleVbProjects(repositoryRoot)];

private static IReadOnlyList<CorpusProject> Scan(
    string repositoryRoot,
    string[] corpusSegments,
    string searchPattern,
    string description)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

    var fullRepositoryRoot = Path.GetFullPath(repositoryRoot);
    var corpusRoot = Path.Combine(
        [fullRepositoryRoot, "examples", "language-features", .. corpusSegments]);
    if (!Directory.Exists(corpusRoot))
    {
        throw new DirectoryNotFoundException($"The {description} directory does not exist: {corpusRoot}.");
    }

    return Directory.EnumerateFiles(corpusRoot, searchPattern, SearchOption.AllDirectories)
        .Where(path => !HasExcludedDirectory(corpusRoot, path))
        .Select(ReadProject)
        .Where(project => project.IsSdkStyle && project.IsLibrary && !project.AllowsUnsafeBlocks)
        .Select(project => new CorpusProject(
            Path.GetRelativePath(fullRepositoryRoot, project.Path).Replace('\\', '/'),
            project.TargetFramework!,
            project.LanguageVersion!))
        .OrderBy(project => project.RepositoryRelativePath, StringComparer.Ordinal)
        .ToArray();
}
```

`ReadProject`, `HasExcludedDirectory`, and the validation helpers are unchanged and are reused as-is.

- [ ] **Step 4: Run the test to verify it passes**

Expected: PASS. `FindSdkStyleLibrariesReturnsTheCompleteSortedCorpusMatrix` must still pass unchanged — the C# path's behavior is identical.

- [ ] **Step 5: Build every VB project from the suite**

In `CorpusProjectBuildTests.ProjectCoordinates`, switch the source and derive the language label:

```csharp
public static IEnumerable<object[]> ProjectCoordinates()
{
    var repositoryRoot = RepositoryRoot();
    foreach (var project in CorpusProjectDiscovery.FindAllSdkStyleProjects(repositoryRoot))
    {
        var language = project.RepositoryRelativePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
            ? "VB"
            : "C#";
        yield return
        [
            Path.GetFullPath(
                project.RepositoryRelativePath.Replace('/', Path.DirectorySeparatorChar),
                repositoryRoot),
            $"{project.TargetFramework}/{language} {project.LanguageVersion}"
        ];
    }
}
```

- [ ] **Step 6: Run the build tests**

Run `CorpusProjectBuildTests` through the private host.

Expected: PASS for every project, C# and VB. Each asserts `0 Warning(s)` and `0 Error(s)`. The suite gets noticeably slower — one `-t:Rebuild` per pin per family.

- [ ] **Step 7: Commit**

```bash
git add tests/DotNetKnowledge.Corpus.Tests
git commit -m "test: build every VB corpus project at zero warnings

Discovery scanned only CSharp/dotnet for csproj, so no VB project was
ever built by the suite. A pinned LangVersion proves nothing unless
something builds it."
```

---

### Task 4: Guard against rows no project compiles

A shared source tree makes cross-project drift structurally impossible, and introduces the opposite hazard: a row added to `src/` that no project globs is in the corpus and compiled by nothing.

**Files:**
- Create: `tests/DotNetKnowledge.Corpus.Tests/Projects/VbSourceCoverageTests.cs`

**Interfaces:**
- Consumes: the on-disk layout from Tasks 2 and 5. This test reads project XML directly rather than going through `CorpusProjectDiscovery`, because it needs the `Compile` items, which `CorpusProject` does not carry.
- Produces: nothing other tasks depend on.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Xml.Linq;

namespace DotNetKnowledge.Corpus.Tests.Projects;

[TestClass]
[TestCategory("Unit")]
public sealed class VbSourceCoverageTests
{
    [TestMethod]
    public void EveryVbRowFolderIsCompiledByAtLeastOneProject()
    {
        var repositoryRoot = RepositoryRoot();
        var uncovered = new List<string>();

        foreach (var familyRoot in VbFamilyRoots(repositoryRoot))
        {
            var sourceRoot = Path.Combine(familyRoot, "src");
            var covered = CompiledDirectories(familyRoot);

            foreach (var versionDir in Directory.EnumerateDirectories(sourceRoot))
            {
                foreach (var rowDir in Directory.EnumerateDirectories(versionDir))
                {
                    var full = Path.GetFullPath(rowDir);
                    if (!covered.Any(prefix =>
                            full.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                            full.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    {
                        uncovered.Add(Path.GetRelativePath(repositoryRoot, full).Replace('\\', '/'));
                    }
                }
            }
        }

        Assert.IsTrue(
            uncovered.Count == 0,
            $"These row folders are in the corpus but no project compiles them:{Environment.NewLine}" +
            string.Join(Environment.NewLine, uncovered));
    }

    // Every Compile Include ends in a "/**/*.vb" glob tail; the directory in front of it is what
    // the project actually covers.
    private static List<string> CompiledDirectories(string familyRoot)
    {
        var directories = new List<string>();

        foreach (var project in Directory.EnumerateFiles(familyRoot, "*.vbproj", SearchOption.AllDirectories))
        {
            if (project.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                project.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var projectDirectory = Path.GetDirectoryName(project)!;
            foreach (var include in XDocument.Load(project)
                         .Descendants()
                         .Where(element => element.Name.LocalName == "Compile")
                         .Select(element => element.Attribute("Include")?.Value)
                         .Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                var tail = include!.IndexOf("**", StringComparison.Ordinal);
                var directoryPart = tail >= 0 ? include[..tail] : Path.GetDirectoryName(include) ?? "";
                directories.Add(Path.GetFullPath(Path.Combine(projectDirectory, directoryPart)).TrimEnd(Path.DirectorySeparatorChar));
            }
        }

        return directories;
    }

    private static IEnumerable<string> VbFamilyRoots(string repositoryRoot)
    {
        var vbRoot = Path.Combine(repositoryRoot, "examples", "language-features", "VB.NET");
        yield return Path.Combine(vbRoot, "dotnet", "Net10");
        yield return Path.Combine(vbRoot, "dotNetFramework", "v4.8");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "sources.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
```

- [ ] **Step 2: Run the test**

Expected at this point: FAIL, because Task 7 has not created the net48 `src/` tree yet — `VbFamilyRoots` names a directory that does not exist.

Temporarily narrow `VbFamilyRoots` to the net10 family only, confirm PASS, then restore both entries and leave the test failing until Task 7. Note this in the commit message so the next worker is not surprised.

Alternative if you prefer a green suite throughout: defer this task until after Task 7. Nothing else depends on it.

- [ ] **Step 3: Verify the guard actually catches an orphan**

Temporarily rename `src/Vb14/NameOfOperator` to `src/Vb14/NameOfOperatorX` and re-run.

Expected: FAIL, naming `.../src/Vb14/NameOfOperatorX`. Rename it back and confirm PASS.

This step matters: a coverage test that cannot fail is worse than no test, because it reads as protection.

- [ ] **Step 4: Commit**

```bash
git add tests/DotNetKnowledge.Corpus.Tests/Projects/VbSourceCoverageTests.cs
git commit -m "test: fail when a VB row folder is compiled by no project

A shared source tree removes drift between copies and replaces it with
a quieter failure: a row that exists but that nothing builds."
```

---

### Task 5: Restructure the net48 VB family, split `MyType`, and add reference assemblies

**Files:**
- Create: `examples/language-features/VB.NET/dotNetFramework/v4.8/Directory.Build.props`
- Move: the eight version folders → `.../v4.8/src/`
- Create: `.../v4.8/<pin>/library/library.vbproj` + `.slnx` for each of the nine pins
- Create: `.../v4.8/11/my/my.vbproj` + `.slnx`, and `.../v4.8/latest/my/my.vbproj` + `.slnx`
- Modify: `examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v8.0/CSharp80.csproj`
- Delete: `.../v4.8/VbNetFw48.vbproj`, `.../v4.8/VbNetFw48.slnx`, and the stale `bin/`, `obj/`

**Interfaces:**
- Consumes: Task 1's repaired `Baseline`, Task 3's discovery.
- Produces: `RootNamespace` values `Net48_Vb11_Library` … `Net48_VbLatest_Library`, plus `Net48_Vb11_My` and `Net48_VbLatest_My`.

`MyType` is a per-compilation switch that cannot be scoped to a folder — the same reason C# houses its `AllowUnsafeBlocks` and `OutputType` rows in separate `unsafe` and `exe` projects on a sparse ladder. `MyNamespaceHelpers` is treated the same way, so the mainline projects carry no `MyType` at all.

- [ ] **Step 1: Move the sources and drop the old project**

```bash
cd examples/language-features/VB.NET/dotNetFramework/v4.8
mkdir src
git mv Baseline Vb14 Vb15 Vb15_3 Vb15_5 Vb16_0 Vb16_9 Vb17_13 src/
git rm VbNetFw48.vbproj VbNetFw48.slnx
rm -rf bin obj
```

- [ ] **Step 2: Add reference assemblies to `CSharp_v8.0`**

The net48 VB family references this project, and no `net48` project in the repository currently builds without a machine-installed .NET Framework targeting pack. Add to the `ItemGroup` of
`examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v8.0/CSharp80.csproj`:

```xml
    <!-- Supplies the net48 reference assemblies so this project builds without a machine-installed
         .NET Framework targeting pack. PrivateAssets keeps it out of the compile surface. -->
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
```

Verify: `dotnet build examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v8.0/CSharp80.csproj -t:Rebuild --nologo` reports `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 3: Create the family props**

`examples/language-features/VB.NET/dotNetFramework/v4.8/Directory.Build.props`:

```xml
<Project>
  <!--
    MSBuild's automatic discovery stops at the first Directory.Build.props found walking up from
    a project, which is this file. The corpus's zero-warning gate depends on TreatWarningsAsErrors
    arriving from the repository root, so the chain has to be continued explicitly.
  -->
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
  <PropertyGroup>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <!-- Supplies the net48 reference assemblies so these projects build without a machine-installed
         .NET Framework targeting pack. -->
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
    <!--
      System.Memory carries Span for the ref-return row's Span-consumption half. System.Text.Json
      carries JsonSchemaExporterOptions, the init-only-property subject the VB 16.9 row reads
      against; net48's own BCL has no type with an init accessor, because init-only postdates it.
    -->
    <PackageReference Include="System.Memory" Version="4.5.5" />
    <PackageReference Include="System.Text.Json" Version="9.0.0" />
  </ItemGroup>
  <ItemGroup>
    <!--
      The ref-return row consumes a C#-authored ref-returning method. On net10 that subject is
      CollectionsMarshal.GetValueRefOrNullRef from the BCL, which has no net48 backport; here the
      corpus's own C# project supplies one instead.

      Items in a props file are evaluated in the consuming project's context, so this path is
      anchored to this file's directory rather than resolving against each project directory.
    -->
    <ProjectReference Include="$(MSBuildThisFileDirectory)../../CSharp/dotNetFramework/v4.8/CSharp_v8.0/CSharp80.csproj" />
  </ItemGroup>
</Project>
```

These references are unconditional, so the pin-11 project carries references its `Baseline` rows do not use. `$(LangVersion)` is not yet set when props are evaluated, so conditioning them would need a `Directory.Build.targets`. This is consistent with the corpus rule that reference set and `LangVersion` are independent coordinates, and it means **a pinned project is not an era emulation** — the pin constrains the language, not the framework surface.

- [ ] **Step 4: Create the nine mainline projects**

Identical shape to Task 2's table, with `TargetFramework` `net48` and `RootNamespace` prefix `Net48_`. For example `.../v4.8/16.9/library/library.vbproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Net48_Vb16_9_Library</RootNamespace>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>16.9</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../src/Baseline/**/*.vb" LinkBase="Baseline" />
    <Compile Include="../../src/Vb14/**/*.vb" LinkBase="Vb14" />
    <Compile Include="../../src/Vb15/**/*.vb" LinkBase="Vb15" />
    <Compile Include="../../src/Vb15_3/**/*.vb" LinkBase="Vb15_3" />
    <Compile Include="../../src/Vb15_5/**/*.vb" LinkBase="Vb15_5" />
    <Compile Include="../../src/Vb16_0/**/*.vb" LinkBase="Vb16_0" />
    <Compile Include="../../src/Vb16_9/**/*.vb" LinkBase="Vb16_9" />
  </ItemGroup>
  <ItemGroup>
    <!-- MyType=Windows is a per-compilation switch, so the MyNamespaceHelpers row lives in the
         my/ projects instead. Excluding it here keeps this project free of that setting. -->
    <Compile Remove="../../src/Baseline/MyNamespaceHelpers/**/*.vb" />
  </ItemGroup>
</Project>
```

Every mainline net48 project carries that `Compile Remove`, including pin 11.

- [ ] **Step 5: Create the two `my/` projects**

`.../v4.8/11/my/my.vbproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Net48_Vb11_My</RootNamespace>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>11</LangVersion>
    <!--
      MyType=Windows populates the `My` namespace, which is what the MyNamespaceHelpers row
      demonstrates. It is a per-compilation switch that cannot be scoped to a folder, so the row
      is housed apart rather than imposing the setting on every mainline project. On net10.0 the
      SDK passes _MyType=Empty, which is why no net10 project can carry this row at all.
    -->
    <MyType>Windows</MyType>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../src/Baseline/MyNamespaceHelpers/**/*.vb" LinkBase="Baseline/MyNamespaceHelpers" />
  </ItemGroup>
</Project>
```

`.../v4.8/latest/my/my.vbproj` is identical except `RootNamespace` is `Net48_VbLatest_My` and `LangVersion` is `latest`.

A sparse ladder matches how C# treats its `unsafe` and `exe` kinds — those exist at a few pins, not at every one.

- [ ] **Step 6: Create a `.slnx` beside each new project**

Same one-line `<Solution>` shape as Task 2, Step 5, with `Path` naming the sibling `.vbproj`.

- [ ] **Step 7: Build every net48 project**

```bash
for p in 11 14 15 15.3 15.5 16 16.9 17.13 latest; do
  echo "== $p"
  dotnet build "examples/language-features/VB.NET/dotNetFramework/v4.8/$p/library/library.vbproj" -t:Rebuild --nologo -v:minimal
done
dotnet build "examples/language-features/VB.NET/dotNetFramework/v4.8/11/my/my.vbproj" -t:Rebuild --nologo -v:minimal
dotnet build "examples/language-features/VB.NET/dotNetFramework/v4.8/latest/my/my.vbproj" -t:Rebuild --nologo -v:minimal
```

Expected: `0 Warning(s)` and `0 Error(s)` throughout.

Note that `src/Vb17_13` in this family holds only `UnmanagedConstraintRecognition`. The other two `Vb17_13` rows are net10-only, for the capability reasons `MANIFEST.md` records — do not add them here.

- [ ] **Step 8: Run the full corpus test suite**

Task 3's discovery picks these up with no further change, and Task 4's coverage test should now pass with both families listed.

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add -A examples/language-features/VB.NET/dotNetFramework/v4.8 examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v8.0/CSharp80.csproj
git commit -m "feat: pin the net48 VB corpus and free it from MyType

Mainline projects drop MyType entirely; MyNamespaceHelpers moves to a
sparse my/ kind, the way C# houses its unsafe and exe rows. Adding the
reference-assemblies package lets these build without a machine-installed
.NET Framework targeting pack."
```

---

### Task 6: Make `verify-feature-floors.cs` locale-proof and language-parameterized

This is a pure refactor. The C# output must be byte-identical before and after.

**Files:**
- Modify: `scripts/verify-feature-floors.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: a `LanguageProfile` record consumed by Tasks 7 and 8:

```csharp
record LanguageProfile(
    string Name,               // "C#" or "VB"
    string SourceExtension,    // ".cs" or ".vb"
    string ProjectExtension,   // ".csproj" or ".vbproj"
    IReadOnlyList<string> Ladder,
    Func<string, string?> FolderVersion,     // folder name -> ladder value, null if not a version folder
    Func<string, string?> LangVersionArg,    // ladder value -> compiler switch value, null if unspellable
    Func<string, bool> IsEnvironmentError);  // diagnostic text -> true when it is a toolchain fault
```

- [ ] **Step 1: Capture the current C# output as a characterization baseline**

```bash
dotnet scripts/verify-feature-floors.cs -- --json > "$TEMP/floors-before.json"
```

This takes a while — it compiles every group folder at several language versions and downloads `Microsoft.Net.Compilers` 1.3.2 on first run. Keep the file; Step 4 compares against it.

- [ ] **Step 2: Replace literal diagnostic matching with a code regex**

In `Compile` (around `scripts/verify-feature-floors.cs:455`), the first-error scan matches the literal `": error "`. The in-box compilers emit localized text on a non-English machine, and VB's severity token is not guaranteed to stay English. Replace the extraction with:

```csharp
// Diagnostic severity words are localized; the CSnnnn / BCnnnn code is not. Keying on the code
// keeps this correct on a non-English machine, where matching ": error " silently finds nothing
// and a failed compile reads as a clean one.
var diagnosticCode = new Regex(@"\b(?:CS|BC)\d{4}\b");
var firstError = output
    .Split('\n')
    .Select(line => line.Trim())
    .FirstOrDefault(line => diagnosticCode.IsMatch(line))
    ?? (process.ExitCode == 0 ? "" : "no diagnostic text");
```

Keep the existing path-trimming that follows, but guard it: it currently slices from `": error "`, which may now be absent. Trim from the first match of `diagnosticCode` instead.

Add `using System.Text.RegularExpressions;` if it is not already present.

- [ ] **Step 3: Introduce `LanguageProfile` and thread it through**

Move the existing `Versions.Ladder`, `ParseFolderVersion`, `LangVersionArg`, and `IsEnvironmentError` into a `CSharpProfile` static instance of the record above. Change `ProbeFloor`, `Below`, and the main loop to take a `LanguageProfile` parameter instead of calling those free functions directly.

Leave `ExemptionReason`, `LegacyLangVersionArg`, and the period-compiler escalation alone for now — Task 8 generalizes those.

- [ ] **Step 4: Verify the C# output is unchanged**

```bash
dotnet scripts/verify-feature-floors.cs -- --json > "$TEMP/floors-after.json"
diff "$TEMP/floors-before.json" "$TEMP/floors-after.json" && echo IDENTICAL
```

Expected: `IDENTICAL`. Any difference is a refactoring bug, not an improvement.

- [ ] **Step 5: Commit**

```bash
git add scripts/verify-feature-floors.cs
git commit -m "refactor: parameterize the floor probe by language

Also keys diagnostics on the CSnnnn/BCnnnn code rather than the literal
': error '. Severity words are localized; on a non-English machine the
old match found nothing and a failed compile read as a clean one."
```

---

### Task 7: Add the VB ladder to `verify-feature-floors.cs`

**Files:**
- Modify: `scripts/verify-feature-floors.cs`

**Interfaces:**
- Consumes: Task 6's `LanguageProfile`; Tasks 2 and 5's project layout.
- Produces: `--language vb` output that Task 9 uses to place rows.

- [ ] **Step 1: Add the VB profile**

```csharp
// vbc accepts these and rejects 17 and 17.0 with BC2014 — there is no VB 17.0 language version.
// Note the bare "16": VB spells that rung without a minor part.
static readonly string[] VbLadder =
    ["9", "10", "11", "12", "14", "15", "15.3", "15.5", "16", "16.9", "17.13"];

// Corpus folders are namespace segments, so they carry the version with an underscore and a Vb
// prefix: Vb15_3 -> 15.3, Vb16_0 -> 16. Baseline has no single version and is handled as EXEMPT.
static string? VbFolderVersion(string folderName)
{
    if (!folderName.StartsWith("Vb", StringComparison.Ordinal))
    {
        return null;
    }

    var candidate = folderName["Vb".Length..].Replace('_', '.');
    if (candidate.EndsWith(".0", StringComparison.Ordinal))
    {
        candidate = candidate[..^2];
    }

    return VbLadder.Contains(candidate) ? candidate : null;
}
```

Assemble these into a `LanguageProfile` instance beside the C# one from Task 6:

```csharp
static readonly LanguageProfile VbProfile = new(
    Name: "VB",
    SourceExtension: ".vb",
    ProjectExtension: ".vbproj",
    Ladder: VbLadder,
    FolderVersion: VbFolderVersion,
    // Every VB ladder value is spelled the same on the command line, unlike C#'s ISO-1 / ISO-2.
    LangVersionArg: version => version,
    IsEnvironmentError: IsVbEnvironmentError);
```

Select the profile from a new `--language` argument, defaulting to C# so the existing invocations in `CLAUDE.md` and `AGENTS.md` keep working unchanged.

- [ ] **Step 2: Add VB environment-error codes**

An incomplete reference set produces ordinary binding errors that look exactly like language gating. Without this guard the probe manufactures an `UNGATED` verdict out of a broken toolchain.

```csharp
static bool IsVbEnvironmentError(string diagnostic)
{
    string[] codes =
    [
        "BC2017",  // could not find library
        "BC2001",  // file could not be found
        "BC2008",  // no input sources specified
        "BC30002", // type is not defined
        "BC30451", // name is not declared
        "BC31091", // import of type from assembly failed
    ];

    return codes.Any(code => diagnostic.Contains(code, StringComparison.Ordinal));
}
```

`BC30002` and `BC30451` are included deliberately. They are ordinary "missing type" errors, and with a correctly resolved reference set they should not appear — so treating them as environment faults is safer than reading them as evidence that a feature is version-gated.

- [ ] **Step 3: Discover VB projects and exempt `Baseline`**

The existing main loop enumerates `CSharp_v*` directories under the net48 C# root. Generalize it to take a corpus root and a project glob from the profile, and add the VB roots:

- `examples/language-features/VB.NET/dotnet/Net10` — projects at `<pin>/library/library.vbproj`
- `examples/language-features/VB.NET/dotNetFramework/v4.8` — same, plus `<pin>/my/my.vbproj`

Version folders come from `src/`, not from the project directory. Read each project's `Compile Include` items to learn which row folders it owns, the same parsing Task 4's test does.

Add to `ExemptionReason`:

```csharp
    "Baseline" =>
        "the Baseline bucket spans VS.NET 2002 to VS2012, and the upstream sources give no "
        + "per-version attribution below VB 14. No single previous-version pin is meaningful for "
        + "it, so it gets the own-version check only",
```

- [ ] **Step 4: Invoke `vbc` correctly**

`Compile` currently writes a C#-shaped response file. VB needs `/nostdlib` — not `/nostdlib+`, which it rejects with `BC2007` — and an explicit `/vbruntime:` whenever `/nostdlib` is set. Without the latter, every probe fails with `BC2017: could not find library`, which looks like a broken corpus rather than a broken invocation.

Branch the response-file construction on the profile:

```csharp
if (profile.Name == "VB")
{
    rsp.AppendLine("/nostdlib");

    // With /nostdlib the compiler no longer supplies the VB runtime itself, and every probe
    // fails with BC2017 unless it is named explicitly.
    var vbRuntime = references.FirstOrDefault(reference =>
        Path.GetFileName(reference).Equals("Microsoft.VisualBasic.dll", StringComparison.OrdinalIgnoreCase));
    if (vbRuntime is null)
    {
        return new CompileResult(false, "BC2017: no Microsoft.VisualBasic.dll in the resolved reference set");
    }

    rsp.AppendLine($"/vbruntime:\"{vbRuntime}\"");
}
else
{
    rsp.AppendLine("/nostdlib+");
}
```

`ResolveProjectInputs` already resolves a project's full reference set through MSBuild, which is what keeps this correct — a hand-assembled reference set produces exactly the `BC30002`/`BC30451` noise Step 2 guards against, and that noise is indistinguishable from real version gating in a probe log.

- [ ] **Step 5: Run the VB probe**

```bash
dotnet scripts/verify-feature-floors.cs -- --language vb --json > "$TEMP/vb-floors.json"
```

Expected: every row classified. No `MISPLACED` and no `NOT-VERSION-SPECIFIC` — those fail the run.

Sanity-check two known results before trusting the rest:

- `PrivateProtectedAccessModifier` must be **gated** at 15.5. Probed in isolation it fails at 14 with `BC36716: Visual Basic 14.0 does not support Private Protected` and passes at 15.5. If the probe reports it ungated, the probe is wrong.
- `UnmanagedConstraintRecognition` must be **ungated**. It compiles on compilers that predate the `unmanaged` constraint entirely, because older VB ignores the constraint rather than rejecting it.

- [ ] **Step 6: Verify the C# path still works**

```bash
dotnet scripts/verify-feature-floors.cs -- --json > "$TEMP/floors-after2.json"
diff "$TEMP/floors-before.json" "$TEMP/floors-after2.json" && echo IDENTICAL
```

Expected: `IDENTICAL`.

- [ ] **Step 7: Commit**

```bash
git add scripts/verify-feature-floors.cs
git commit -m "feat: classify VB feature floors

Baseline is exempt: it spans VS.NET 2002 to VS2012 and the upstream
sources give no per-version attribution below VB 14, so no single
previous-version pin is meaningful for it."
```

---

### Task 8: Escalate VB floors to native compiler ceilings

A pin restricts a modern compiler only where Roslyn's binder checks feature availability. Elsewhere it admits whatever the installed SDK can already do. So a floor from `/langversion:` alone means "the current SDK does not gate this here" — a fact about the installed toolchain that drifts as SDKs ship. Only a compiler whose *native* ceiling is the version in question can say the feature genuinely was not available then.

**Files:**
- Modify: `scripts/verify-feature-floors.cs`

**Interfaces:**
- Consumes: Task 7's VB profile.
- Produces: floor verdicts marked as native-confirmed or SDK-observed, which Task 10 records in `MANIFEST.md`.

- [ ] **Step 1: Add the VB escalation table**

VB's escalation is better than C#'s, because a native VB 14 compiler sits directly beneath VB's entire post-14 delta:

| Ladder value | Compiler | Notes |
|---|---|---|
| `14` | `.artifacts/period-compilers/microsoft.net.compilers.1.3.2/tools/vbc.exe` | Already downloaded and cached for the C# 6 floor. Reports "for Visual Basic 2012"-era behavior at its own ceiling of VB 14. |
| `11` | `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\vbc.exe` | Identifies itself as "for Visual Basic 2012". |
| `9` | `%WINDIR%\Microsoft.NET\Framework64\v3.5\vbc.exe` | |
| `8` | `%WINDIR%\Microsoft.NET\Framework64\v2.0.50727\vbc.exe` | Below the ladder; useful only as a floor for `Baseline`, which is exempt. |

VB 10 and 12 have no compiler on a modern machine and report `UNPROVEN`, the same way C# 4 and C# 1.x do.

- [ ] **Step 2: Record which kind of evidence each floor rests on**

Add a field to the JSON output distinguishing a floor confirmed against a native ceiling from one observed only under the current SDK. Do not collapse them into one number — the distinction is the whole point of the escalation.

- [ ] **Step 3: Verify against the two known results**

Run the VB probe again. `UnmanagedConstraintRecognition` must come back ungated **with native confirmation** — it compiles on the cached VB 14 compiler, which predates the `unmanaged` constraint. Every row whose floor is above 14 can only be SDK-observed, because no native compiler exists above that rung; the output must say so rather than implying a stronger claim.

- [ ] **Step 4: Verify the C# path is still byte-identical**

```bash
dotnet scripts/verify-feature-floors.cs -- --json > "$TEMP/floors-after3.json"
diff "$TEMP/floors-before.json" "$TEMP/floors-after3.json" && echo IDENTICAL
```

If the C# JSON gained the new evidence field, that is an intended change — regenerate the baseline once, review the diff line by line to confirm nothing else moved, and note it in the commit message.

- [ ] **Step 5: Commit**

```bash
git add scripts/verify-feature-floors.cs
git commit -m "feat: confirm VB floors against native compiler ceilings

A floor from /langversion: alone is a fact about the installed SDK and
drifts as SDKs ship. Microsoft.Net.Compilers 1.3.2 gives a native VB 14
ceiling directly beneath VB's post-14 delta, and the in-box Framework64
compilers give VB 11, 9 and 8. VB 10 and 12 have no compiler and report
UNPROVEN."
```

---

### Task 9: Add the ungated above-pin rows to each project

Tasks 2 and 5 built each project from the at-or-below-pin row set. A project should hold **every row that compiles at its pin**, including rows filed above it whose feature `LangVersion` does not gate — that is the rule the C# tree follows, and the green build is what makes the ungatedness a checked fact rather than a note.

**Files:**
- Modify: every `library.vbproj` under both VB families

**Interfaces:**
- Consumes: Task 7 and 8's probe output.
- Produces: the final row sets.

- [ ] **Step 1: Derive the placement from the probe**

```bash
dotnet scripts/verify-feature-floors.cs -- --language vb --json > "$TEMP/vb-floors.json"
```

For each row, read its measured floor. Add the row's `Compile` glob to every pin at or above that floor.

**Use the probe's output, not the table in the spec.** That table was derived from whole-project builds, which VB truncates — `docs/HANDOFF.md` records "2 errors where per-folder builds reported 5" — and it already produced one wrong entry.

- [ ] **Step 2: Add the per-row globs**

For a row whose floor is below its own version folder, add a per-row glob rather than a whole-folder one. Example, for `14/library/library.vbproj`:

```xml
    <!-- Rows above this pin that LangVersion does not gate; the green build is the evidence. -->
    <Compile Include="../../src/Vb17_13/UnmanagedConstraintRecognition/**/*.vb" LinkBase="Vb17_13/UnmanagedConstraintRecognition" />
```

Where a rung takes an entire version folder, keep the whole-folder glob.

- [ ] **Step 3: Rebuild every VB project**

```bash
for f in "dotnet/Net10" "dotNetFramework/v4.8"; do
  for p in 11 14 15 15.3 15.5 16 16.9 17.13 latest; do
    dotnet build "examples/language-features/VB.NET/$f/$p/library/library.vbproj" -t:Rebuild --nologo -v:minimal
  done
done
```

Expected: `0 Warning(s)` and `0 Error(s)` throughout. A `BC36716` here means the probe and the glob disagree — trust the probe and remove the glob.

- [ ] **Step 4: Run the full suite**

Expected: PASS, including Task 4's coverage test.

- [ ] **Step 5: Commit**

```bash
git add examples/language-features/VB.NET
git commit -m "feat: place ungated VB rows at every pin that compiles them

A project holds every row that compiles at its pin, not only those at or
below it. Where LangVersion does not gate a feature, the green build at a
lower pin is what records that."
```

---

### Task 10: Add the inverse VB rule to the namespace verifier

**Files:**
- Modify: `scripts/verify-project-namespaces.cs`

**Interfaces:**
- Consumes: Tasks 2 and 5's `src/` trees.
- Produces: nothing other tasks depend on.

`verify-project-namespaces.cs` already accepts `.vbproj` and requires a `RootNamespace`, so the new projects are covered with no change. What is missing is the opposite check.

- [ ] **Step 1: Write the failing check**

Temporarily edit one file under `examples/language-features/VB.NET/dotnet/Net10/src/Vb14/NameOfOperator/` so its namespace reads `Namespace Net10_Vb14_Library.Vb14.NameOfOperator`.

Run: `dotnet scripts/verify-project-namespaces.cs`

Expected before the fix: exit 0 — the drift goes unreported.

- [ ] **Step 2: Add the rule**

A `.vb` file under a family `src/` must **not** begin its namespace with a `Net10_` or `Net48_` prefix. VB prepends `RootNamespace` itself, so such a file compiles to `Net10_Vb14_Library.Net10_Vb14_Library.Vb14.NameOfOperator` — and because the tree is shared, one such file corrupts every pin at once.

The script currently skips `.vb` files entirely after Rule 1. Replace that skip with a scan, using the same `Finding` shape the rest of the script emits:

```csharp
// VB inherits the project prefix at compile time, so a source file must NOT also declare it.
// A file that does compiles to a doubled namespace, and because the VB families share one
// source tree, a single such file corrupts every pin that globs it.
if (projectPath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
{
    foreach (var source in vbSources)
    {
        var declared = FirstNamespaceSegment(source);
        if (declared is null)
        {
            continue;
        }

        if (declared.StartsWith("Net10_", StringComparison.Ordinal) ||
            declared.StartsWith("Net48_", StringComparison.Ordinal))
        {
            findings.Add(new Finding(
                "vb-project-prefixed-namespace",
                sourceRelative,
                $"declares namespace {declared}, but VB prepends <RootNamespace> itself; this "
                + "compiles to a doubled namespace and corrupts every pin sharing this source"));
        }
    }

    continue;
}
```

Scan each family's `src/` tree once rather than per project — the same file belongs to several projects, and reporting it once per pin would bury the finding.

- [ ] **Step 3: Verify the check fires, then revert the sabotage**

Run: `dotnet scripts/verify-project-namespaces.cs`

Expected: exit 1, naming the sabotaged file. Revert the edit and confirm exit 0.

- [ ] **Step 4: Commit**

```bash
git add scripts/verify-project-namespaces.cs
git commit -m "feat: reject a project-prefixed namespace in VB corpus sources

VB prepends RootNamespace, so a file that also declares it compiles to a
doubled namespace. Under a shared source tree one such file corrupts
every pin at once."
```

---

### Task 11: Update the manifest and documentation

**Files:**
- Modify: `examples/language-features/MANIFEST.md`
- Modify: `AGENTS.md`
- Modify: `CLAUDE.md`
- Modify: `docs/HANDOFF.md`
- Modify: `docs/design/language-feature-showcase-design.md`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

- [ ] **Step 1: Redefine the VB target-project labels and add the floor column**

`MANIFEST.md`'s `Target Projects` column cannot enumerate a project per pin. Redefine `VbFw48` and `VbLatest` to name the two **families**, and update the legend near the top of the file accordingly.

Add a measured-floor column recording the lowest pin at which each row compiles, **together with whether that floor was confirmed against a native compiler ceiling or only observed under the current SDK**. Populate it from Task 8's output. Keeping the two strengths of evidence distinguishable is the point of the column.

- [ ] **Step 2: Correct the byte-identity claim**

Replace the sentence beginning *"The two VB projects' sources are byte-identical"* with a description of the shared per-family `src/` tree and the four genuine divergences: `MyNamespaceHelpers` (net48 only), `ConsumingCSharpRefReturnValues` (present in both, different subject), and `CallerArgumentExpressionConsumption` and `OverloadResolutionPriorityConsumption` (net10 only). The current sentence names two of the four.

- [ ] **Step 3: Remove fixed counts from the documentation**

A count is stale the moment a row or project is added. Remove them rather than correcting them:

- `MANIFEST.md`'s VB and C# section headings, which state row and version totals. The VB heading also miscounts its point versions.
- `AGENTS.md`, which states how many rows the corpus holds and how many projects the build matrix discovers.
- `docs/HANDOFF.md`, which repeats the project count.

Describe what the thing is instead of how many there are — "the SDK-style C# and VB library projects" rather than a number.

Leave the hardcoded expected list in `CorpusProjectDiscoveryTests` alone. That is a test assertion, and being exact is its job.

- [ ] **Step 4: Update `CLAUDE.md`**

Update the corpus-layout section to show the VB families' `src/` and pin structure, and extend the paragraph on VB's namespace exemption with Task 10's inverse rule. Note that `MyType=Windows` now lives only in the `my/` kind, and that the net48 families carry the reference-assemblies package.

- [ ] **Step 5: Update the showcase design doc**

`docs/design/language-feature-showcase-design.md` already predicted the `Baseline` problem and carries a probe-exempt table. Update its VB cautions with the measured result that many post-14 VB rows are ungated, and reconcile the exempt table with Task 8's output.

Do **not** add `PrivateProtectedAccessModifier` to that table. It is gated at 15.5; an earlier whole-project probe suggested otherwise and was wrong.

- [ ] **Step 6: Run every guard**

```bash
dotnet scripts/verify-no-vendored-content.cs
dotnet scripts/verify-project-namespaces.cs
dotnet scripts/verify-feature-floors.cs
dotnet scripts/verify-feature-floors.cs -- --language vb
```

Then run the full corpus test suite through the private host.

Expected: all clean, suite green.

- [ ] **Step 7: Commit**

```bash
git add examples/language-features/MANIFEST.md AGENTS.md CLAUDE.md docs/HANDOFF.md docs/design/language-feature-showcase-design.md
git commit -m "docs: record the VB pin ladder and how its floors were measured

The manifest's floor column distinguishes a floor confirmed against a
native compiler ceiling from one observed only under the current SDK.
Fixed counts are removed rather than corrected; they go stale as soon as
a row or project is added."
```

---

## Definition of Done

1. Every VB project builds at 0 errors and 0 warnings through `CorpusProjectBuildTests`.
2. `dotnet scripts/verify-project-namespaces.cs` is clean, including the inverse VB rule.
3. `dotnet scripts/verify-feature-floors.cs -- --language vb` reports no `MISPLACED` and no `NOT-VERSION-SPECIFIC`.
4. `VbSourceCoverageTests` passes, and has been shown to fail when a row is orphaned.
5. `dotnet scripts/verify-no-vendored-content.cs` is clean.
6. The C# half of the suite is untouched and still green, and the C# floor JSON is unchanged apart from the deliberate evidence field.

## Known Risks

- **net48 floors are measured, not inherited.** Probe that family independently; it carries an extra row and a different ref-return subject.
- **Whole-project probing under-reports.** It already produced one wrong floor during design. Probe one row at a time; a result derived any other way is not evidence.
- **An incomplete reference set imitates gating.** `BC30002` and `BC30451` from a broken toolchain look exactly like a version rejection. This is why Task 7 classifies them as environment errors and why `ResolveProjectInputs` is used instead of a hand-assembled reference set.
- **Linux buildability is unverified.** The reference-assemblies package is the known prerequisite. Whether `MyType=Windows` survives there is open — if it does not, only the two `my/` projects are Windows-gated rather than the whole net48 family. The legacy non-SDK C# net48 projects stay Windows-only regardless, since they need Visual Studio's `MSBuild.exe`.
- **Suite runtime grows** with a project per pin per family. If it becomes slow enough to discourage running, trim the ladder — the rungs that add no rows are the candidates.
