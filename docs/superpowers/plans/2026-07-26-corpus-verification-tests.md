# Corpus Verification Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an automated test harness that proves corpus claims across SDK/compiler versions, target frameworks, language pins, and runtime execution, beginning with the `NumericIntPtr` and “C# 10 targeting net5.0” findings.

**Architecture:** Add one `net10.0` MSTest project whose fast unit tests validate case files and harness logic, while integration tests create isolated temporary projects and invoke an explicitly selected installed SDK through a generated `global.json`. Each checked-in case states its source, SDK band, TFM, language pin, expected build result and diagnostics, plus optional executable output; each SDK band maps to one required exact patch version, and no test infers one axis from another or treats a successful build as runtime evidence.

**Tech Stack:** C# 14, .NET 10, `MSTest.Sdk/4.3.2`, `System.Text.Json`, `System.Diagnostics.Process`, MSBuild, `dotnet test`

## Global Constraints

- Read root `AGENTS.md`, `CLAUDE.md`, `docs/HANDOFF.md`, and `examples/language-features/MANIFEST.md` before implementation.
- Preserve the corpus completion gate: every project must build with 0 errors and 0 warnings, with inherited `TreatWarningsAsErrors=true`.
- Keep SDK/compiler version, `TargetFramework`, `LangVersion`, and runtime execution as four independent test inputs.
- Compile feature rows in isolation with `EnableDefaultCompileItems=false`; whole-project builds do not replace per-row probes.
- Set `GenerateTargetFrameworkAttribute=false` in generated probe projects so ISO language pins do not receive phantom `global::` diagnostics.
- A negative test must assert the diagnostic code attributable to the tested feature; a nonzero exit code alone is insufficient.
- A runtime claim must be verified by executing a generated `Exe` project and comparing its exit code and normalized stdout exactly.
- Missing required SDKs or runtimes fail integration preflight. Tests must never silently skip an absent toolchain.
- Temporary projects live below `Path.GetTempPath()` and are deleted only after their resolved absolute path is verified beneath the harness-owned temporary root.
- Do not add `#pragma warning disable`, override `TreatWarningsAsErrors`, vendor upstream Microsoft source, or commit generated compiler output.
- Repository tooling remains single-file C#; no `.sh`, `.ps1`, `.bat`, or `.py` helper is added.
- American English; LF endings; UTF-8.
- This plan establishes the harness and a vertical slice. A separate follow-up inventory will classify all 169 C# and 58 VB rows as compile-only, runtime-verifiable, externally verifiable, or comments-only before claiming corpus-wide runtime coverage.

---

### Task 1: Test Project and Case Schema

**Files:**
- Create: `tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj`
- Create: `tests/DotNetKnowledge.Corpus.Tests/Cases/CorpusCase.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/Cases/CorpusCaseLoader.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/Cases/CorpusCaseLoaderTests.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/Fixtures/valid-case.json`

**Interfaces:**
- Produces: `CorpusCaseLoader.Load(string path) : CorpusCase`
- Produces: `CorpusCase.Validate(string repositoryRoot) : IReadOnlyList<string>`
- Produces: immutable records `CorpusCase`, `CompilationExpectation`, and `RuntimeExpectation`

- [ ] **Step 1: Create the test project and a failing loader test**

Use the Microsoft-supported MSTest SDK without adding a root `global.json`, because later probes
must be free to select different SDKs:

```xml
<Project Sdk="MSTest.Sdk/4.3.2">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>DotNetKnowledge.Corpus.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Content Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="TestCases\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

The first test must load `Fixtures/valid-case.json` and assert:

```csharp
Assert.AreEqual("Harness.Valid", testCase.Id);
Assert.AreEqual("fixtures/Valid.cs", testCase.Source);
Assert.HasCount(1, testCase.Compilations);
Assert.AreEqual("10.0", testCase.Compilations[0].SdkBand);
Assert.AreEqual("net10.0", testCase.Compilations[0].TargetFramework);
Assert.AreEqual("14.0", testCase.Compilations[0].LanguageVersion);
Assert.AreEqual(BuildOutcome.Success, testCase.Compilations[0].Outcome);
```

Mark schema, parser, validation, and discovery test classes with `[TestCategory("Unit")]`. Mark
tests that launch `dotnet`, MSBuild, or a produced executable with `[TestCategory("Integration")]`;
no test may carry both categories.

- [ ] **Step 2: Run the test and verify the schema is absent**

Run:

```powershell
dotnet test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --filter FullyQualifiedName~CorpusCaseLoaderTests --nologo
```

Expected: build failure because `CorpusCaseLoader`, `CorpusCase`, and `BuildOutcome` do not exist.

- [ ] **Step 3: Implement the immutable schema and strict JSON loader**

Define these exact shapes:

```csharp
internal enum BuildOutcome
{
    Success,
    Failure
}

internal sealed record CorpusCase(
    string Id,
    string Source,
    IReadOnlyList<CompilationExpectation> Compilations,
    IReadOnlyList<RuntimeExpectation> Runtimes);

internal sealed record CompilationExpectation(
    string SdkBand,
    string TargetFramework,
    string LanguageVersion,
    BuildOutcome Outcome,
    IReadOnlyList<string> Diagnostics);

internal sealed record RuntimeExpectation(
    string Harness,
    string SdkBand,
    string TargetFramework,
    string LanguageVersion,
    int ExitCode,
    IReadOnlyList<string> StandardOutput);
```

Configure `JsonSerializerOptions` with camel-case property names,
`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`, and
`UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow`. `Validate` must report every error
in one pass: blank IDs, duplicate compilation coordinates, nonexistent source/harness paths,
failure cases without diagnostics, success cases with diagnostics, and runtime coordinates that
do not have a successful compilation entry.

- [ ] **Step 4: Add malformed-case tests**

Add data-driven tests that assert exact validation messages for:

```text
Case ID is required.
Duplicate compilation coordinate: 10.0|net10.0|14.0.
Failure compilation 10.0|net10.0|13.0 must name at least one diagnostic.
Runtime coordinate 10.0|net10.0|14.0 must have a successful compilation expectation.
Source does not exist: fixtures/Missing.cs.
```

- [ ] **Step 5: Run the loader tests**

Run:

```powershell
dotnet test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --filter FullyQualifiedName~CorpusCaseLoaderTests --nologo
```

Expected: all `CorpusCaseLoaderTests` pass with 0 warnings and 0 errors.

- [ ] **Step 6: Commit**

```powershell
git add tests/DotNetKnowledge.Corpus.Tests
git commit -m "test: add corpus verification case schema"
```

---

### Task 2: SDK and Runtime Inventory

**Files:**
- Create: `tests/DotNetKnowledge.Corpus.Tests/Toolchains/ToolchainInventory.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/Toolchains/ToolchainInventoryTests.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/Toolchains/RequiredToolchainsTests.cs`

**Interfaces:**
- Consumes: `CompilationExpectation.SdkBand`
- Produces: `ToolchainInventory.Discover(string dotnetPath, ProcessRunner runner) : Task<ToolchainInventory>`
- Produces: `ToolchainInventory.ResolveSdk(string band) : InstalledSdk`
- Produces: `ToolchainInventory.HasRuntime(string majorMinor) : bool`
- Produces: records `InstalledSdk(Version Version, string Directory)` and `InstalledRuntime(string Name, Version Version, string Directory)`

- [ ] **Step 1: Write parser tests for real `dotnet --list-*` output shapes**

Use these fixtures in the tests:

```text
5.0.408 [C:\Program Files\dotnet\sdk]
7.0.410 [C:\Program Files\dotnet\sdk]
10.0.302 [C:\Program Files\dotnet\sdk]
```

```text
Microsoft.NETCore.App 5.0.17 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
Microsoft.NETCore.App 7.0.20 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
Microsoft.NETCore.App 10.0.10 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
```

Assert that `ResolveSdk("7.0")` returns `7.0.410` even when another 7.0 patch is installed, and an
unconfigured `6.0` throws:

```text
SDK band 6.0 has no configured exact version. Configured SDKs: 5.0.408, 7.0.410, 10.0.302.
```

- [ ] **Step 2: Run the parser tests and verify they fail**

Run:

```powershell
dotnet test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --filter FullyQualifiedName~ToolchainInventoryTests --nologo
```

Expected: build failure because `ToolchainInventory` is absent.

- [ ] **Step 3: Implement inventory parsing and resolution**

Parse `dotnet --list-sdks` and `dotnet --list-runtimes` without assuming `C:\Program Files\dotnet`.
Reject malformed nonblank lines instead of dropping them. Map bands 5.0, 7.0, and 10.0 to required
versions 5.0.408, 7.0.410, and 10.0.302 respectively, then retain the exact resolved version for
generated `global.json` files and failure messages.

- [ ] **Step 4: Add the integration preflight**

`RequiredToolchainsTests` must load every checked-in case and collect distinct SDK bands and
runtime versions. It must fail once with the complete missing set, for example:

```text
Missing required toolchains:
- Required .NET SDK 5.0.408 for band 5.0 is not installed.
- Microsoft.NETCore.App runtime 5.0
- Required .NET SDK 7.0.410 for band 7.0 is not installed.
- Microsoft.NETCore.App runtime 7.0
```

Do not call `Assert.Inconclusive`, dynamically ignore tests, or roll SDK 7 probes onto SDK 10.
Mark this class `[TestCategory("Integration")]`.

- [ ] **Step 5: Run the unit portion**

Run:

```powershell
dotnet test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --filter "TestCategory=Unit" --nologo
```

Expected: all unit tests pass on Windows with only SDK 10 installed.

- [ ] **Step 6: Commit**

```powershell
git add tests/DotNetKnowledge.Corpus.Tests/Toolchains
git commit -m "test: inventory exact dotnet toolchains"
```

---

### Task 3: Isolated Probe Project Runner

**Files:**
- Create: `tests/DotNetKnowledge.Corpus.Tests/Execution/ProcessRunner.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/Execution/ProcessResult.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/Probes/ProbeProject.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/Probes/ProbeResult.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/Probes/ProbeProjectTests.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/Fixtures/AlwaysValid.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/Fixtures/FileScopedNamespace.cs`

**Interfaces:**
- Consumes: `InstalledSdk`, `CompilationExpectation`, repository-relative source paths
- Produces: `ProcessRunner.RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken) : Task<ProcessResult>`
- Produces: `ProbeProject.BuildAsync(InstalledSdk sdk, CompilationExpectation expectation, string sourcePath, string? harnessPath, CancellationToken cancellationToken) : Task<ProbeResult>`

- [ ] **Step 1: Write a failing project-generation test**

Assert the generated `global.json` contains the exact installed SDK:

```json
{
  "sdk": {
    "version": "10.0.302",
    "rollForward": "disable"
  }
}
```

Assert the generated project contains:

```xml
<TargetFramework>net5.0</TargetFramework>
<LangVersion>10.0</LangVersion>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<EnableDefaultCompileItems>false</EnableDefaultCompileItems>
<GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>
<Compile Include="C:\absolute\path\to\FileScopedNamespace.cs" Link="Subject.cs" />
```

- [ ] **Step 2: Run the test and verify it fails**

Run:

```powershell
dotnet test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --filter FullyQualifiedName~ProbeProjectTests --nologo
```

Expected: build failure because `ProbeProject` is absent.

- [ ] **Step 3: Implement argument-safe process execution**

Use `ProcessStartInfo.ArgumentList`, never a concatenated command string. Capture stdout and stderr
concurrently, enforce a five-minute timeout, kill the process tree on timeout, and include the
executable, arguments, working directory, exit code, stdout, and stderr in `ProcessResult`.

- [ ] **Step 4: Implement probe creation, build, diagnostic extraction, and cleanup**

Create one GUID-named directory beneath a single harness-owned root such as
`Path.Combine(Path.GetTempPath(), "dotnet-knowledge-corpus-tests")`. Before recursive deletion,
resolve both paths with `Path.GetFullPath` and require the probe path to begin with the owned root
plus `Path.DirectorySeparatorChar`.

Build with:

```text
dotnet build probe.csproj -t:Rebuild --nologo -v:minimal
```

Extract unique compiler diagnostic codes with the bounded expression
`(?:^|[\s:])(CS|BC)\d{4}(?=[:\s])`. Preserve the complete process output for assertion failures.

- [ ] **Step 5: Add real positive and negative fixture probes**

Use SDK 10 for both:

- `AlwaysValid.cs`, `net10.0`, C# 14: success.
- `FileScopedNamespace.cs`, `net5.0`, C# 9: failure containing `CS8773`.
- `FileScopedNamespace.cs`, `net5.0`, C# 10: success.

These tests prove that TFM and language pin are independent before testing any corpus row.
Mark the class `[TestCategory("Integration")]`.
Define `FileScopedNamespace.cs` exactly as:

```csharp
namespace Harness;

public static class LanguageMarker
{
    public static string Describe() => "C# 10 on net5.0";
}
```

- [ ] **Step 6: Run the probe tests**

Run:

```powershell
dotnet test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --filter FullyQualifiedName~ProbeProjectTests --nologo
```

Expected: all probe tests pass; the C# 9 case reports `CS8773`, and the two positive cases report no
warnings or errors.

- [ ] **Step 7: Commit**

```powershell
git add tests/DotNetKnowledge.Corpus.Tests/Execution tests/DotNetKnowledge.Corpus.Tests/Probes tests/DotNetKnowledge.Corpus.Tests/Fixtures
git commit -m "test: compile corpus sources in isolated projects"
```

---

### Task 4: Prove the SDK/TFM/Language Matrix

**Files:**
- Create: `tests/DotNetKnowledge.Corpus.Tests/TestCases/ModernCompilerOldTarget.case.json`
- Create: `tests/DotNetKnowledge.Corpus.Tests/TestCases/NumericIntPtr.case.json`
- Create: `tests/DotNetKnowledge.Corpus.Tests/CorpusCompilationTests.cs`

**Interfaces:**
- Consumes: `CorpusCaseLoader`, `ToolchainInventory.ResolveSdk`, `ProbeProject.BuildAsync`
- Produces: one data-driven `CorpusCompilationTests.Build_matches_declared_expectation` result per compilation coordinate

- [ ] **Step 1: Add the C# 10-on-net5 case**

Point `ModernCompilerOldTarget.case.json` at `Fixtures/FileScopedNamespace.cs` and declare:

```json
{
  "id": "Toolchains.ModernCompilerOldTarget",
  "source": "tests/DotNetKnowledge.Corpus.Tests/Fixtures/FileScopedNamespace.cs",
  "compilations": [
    {
      "sdkBand": "5.0",
      "targetFramework": "net5.0",
      "languageVersion": "10.0",
      "outcome": "failure",
      "diagnostics": ["CS1617"]
    },
    {
      "sdkBand": "10.0",
      "targetFramework": "net5.0",
      "languageVersion": "9.0",
      "outcome": "failure",
      "diagnostics": ["CS8773"]
    },
    {
      "sdkBand": "10.0",
      "targetFramework": "net5.0",
      "languageVersion": "10.0",
      "outcome": "success",
      "diagnostics": []
    }
  ],
  "runtimes": []
}
```

This is the mechanical proof that the successful combination is not an “SDK 5 compatibility
compiler”: SDK 5 rejects the language-version value, while SDK 10 accepts C# 10 and binds against
the net5.0 reference pack.

- [ ] **Step 2: Add the NumericIntPtr case**

Point the case at:

```text
examples/language-features/CSharp/dotnet/10/latest/library/CSharp11/NumericIntPtr/NumericIntPtr.cs
```

Declare these coordinates:

```text
SDK 7.0  | net7.0  | C# 11.0 | failure | CS0266
SDK 10.0 | net6.0  | C# 11.0 | failure | CS0266, CS0019, CS9135
SDK 10.0 | net7.0  | C# 10.0 | success |
SDK 10.0 | net10.0 | C# 10.0 | success |
SDK 10.0 | net10.0 | C# 11.0 | success |
```

The SDK 7 expectation records the user-observed result and deliberately prevents the harness from
rewriting it into a language-version story. If a fully patched SDK 7 produces a different result,
stop at the red test, quote its exact compiler version and diagnostics, and correct the authored
expectation and `NumericIntPtr.cs` comment together; never weaken the assertion to “either result.”

- [ ] **Step 3: Write the data-driven compilation test**

For success, assert exit code zero and no `warning ` or `error ` tokens in build output. For failure,
assert nonzero exit and every expected diagnostic code. Include this coordinate in the display
name and failure:

```text
{case.Id} [SDK {resolvedSdk.Version}, {targetFramework}, C# {languageVersion}]
```

- [ ] **Step 4: Run the matrix and capture the first authoritative SDK 7 result**

Run:

```powershell
dotnet test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --filter FullyQualifiedName~CorpusCompilationTests --nologo --logger "console;verbosity=detailed"
```

Expected with SDKs 5, 7, and 10 installed: eight passing compilation coordinates. If the SDK 7
coordinate differs, follow Step 2's stop-and-correct rule before proceeding.

- [ ] **Step 5: Commit**

```powershell
git add tests/DotNetKnowledge.Corpus.Tests/TestCases tests/DotNetKnowledge.Corpus.Tests/CorpusCompilationTests.cs
git commit -m "test: prove compiler target and language boundaries"
```

---

### Task 5: Execute NumericIntPtr Behavior

**Files:**
- Create: `tests/DotNetKnowledge.Corpus.Tests/TestCases/ModernCompilerOldTarget.Program.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/TestCases/NumericIntPtr.Program.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/CorpusRuntimeTests.cs`
- Modify: `tests/DotNetKnowledge.Corpus.Tests/TestCases/ModernCompilerOldTarget.case.json`
- Modify: `tests/DotNetKnowledge.Corpus.Tests/TestCases/NumericIntPtr.case.json`

**Interfaces:**
- Consumes: `RuntimeExpectation`, `ProbeProject.BuildAsync`
- Produces: `ProbeProject.RunAsync(InstalledSdk sdk, ProbeResult successfulBuild, CancellationToken cancellationToken) : Task<ProcessResult>`
- Produces: `CorpusRuntimeTests.Runtime_output_matches_declared_expectation`

- [ ] **Step 1: Add executable harnesses with deterministic output**

For the old-target case:

```csharp
using Harness;

Console.WriteLine(LanguageMarker.Describe());
```

For NumericIntPtr, use the exact public API from the corpus row:

```csharp
using System;
using CSharpNet10Latest.CSharp11.NumericIntPtr;

Console.WriteLine($"From constant: {NumericPointerSized.FromConstant()}");
Console.WriteLine($"Multiply: {NumericPointerSized.Multiply((IntPtr)6, (IntPtr)7)}");
Console.WriteLine(
    $"Classify: {NumericPointerSized.Classify((IntPtr)0)}, " +
    $"{NumericPointerSized.Classify((IntPtr)1)}, " +
    $"{NumericPointerSized.Classify((IntPtr)2)}");
```

- [ ] **Step 2: Declare the runtime expectations**

Add this array to `ModernCompilerOldTarget.case.json`:

```json
[
  {
    "harness": "tests/DotNetKnowledge.Corpus.Tests/TestCases/ModernCompilerOldTarget.Program.cs",
    "sdkBand": "10.0",
    "targetFramework": "net5.0",
    "languageVersion": "10.0",
    "exitCode": 0,
    "standardOutput": ["C# 10 on net5.0"]
  }
]
```

Add this array to `NumericIntPtr.case.json`:

```json
[
  {
    "harness": "tests/DotNetKnowledge.Corpus.Tests/TestCases/NumericIntPtr.Program.cs",
    "sdkBand": "10.0",
    "targetFramework": "net7.0",
    "languageVersion": "10.0",
    "exitCode": 0,
    "standardOutput": [
      "From constant: 42",
      "Multiply: 42",
      "Classify: zero, one, other"
    ]
  },
  {
    "harness": "tests/DotNetKnowledge.Corpus.Tests/TestCases/NumericIntPtr.Program.cs",
    "sdkBand": "10.0",
    "targetFramework": "net10.0",
    "languageVersion": "11.0",
    "exitCode": 0,
    "standardOutput": [
      "From constant: 42",
      "Multiply: 42",
      "Classify: zero, one, other"
    ]
  }
]
```

- [ ] **Step 3: Write the failing runtime test**

The test must compile the row and harness into one executable probe, run the already-built DLL
without rebuilding, normalize only CRLF to LF and one final newline, and compare all output lines
in order. It must also assert stderr is empty and exit code is zero.

- [ ] **Step 4: Run the test and verify it fails before runtime execution exists**

Run:

```powershell
dotnet test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --filter FullyQualifiedName~CorpusRuntimeTests --nologo
```

Expected: failure because `ProbeProject.RunAsync` does not yet launch the generated executable.

- [ ] **Step 5: Implement runtime launch**

Invoke:

```text
dotnet <absolute-path-to-probe.dll>
```

from the probe output directory, using the selected SDK's `dotnet` host and the same five-minute
timeout. Do not use `dotnet run`, because that performs another build and can change the evidence
between compilation and execution.

- [ ] **Step 6: Run the runtime test**

Run:

```powershell
dotnet test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --filter FullyQualifiedName~CorpusRuntimeTests --nologo --logger "console;verbosity=detailed"
```

Expected exact stdout includes:

```text
C# 10 on net5.0
From constant: 42
Multiply: 42
Classify: zero, one, other
```

The three NumericIntPtr lines must appear once for net7.0 and once for net10.0. Expected: exit code
0, empty stderr, 0 warnings, and all three runtime coordinates passing.

- [ ] **Step 7: Commit**

```powershell
git add tests/DotNetKnowledge.Corpus.Tests
git commit -m "test: execute NumericIntPtr behavior"
```

---

### Task 6: Build Every SDK-Style Corpus Project

**Files:**
- Create: `tests/DotNetKnowledge.Corpus.Tests/Projects/CorpusProjectDiscovery.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/Projects/CorpusProjectDiscoveryTests.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/CorpusProjectBuildTests.cs`

**Interfaces:**
- Produces: `CorpusProjectDiscovery.FindSdkStyleLibraries(string repositoryRoot) : IReadOnlyList<string>`
- Consumes: `ProcessRunner`

- [ ] **Step 1: Write discovery tests**

Assert discovery includes all SDK-style `library.csproj` files beneath
`examples/language-features/CSharp/dotnet/`, sorted by repository-relative path, and excludes
`obj`, `bin`, `.artifacts`, the COM support project, unsafe projects, executable projects, and
legacy net48 projects. Assert the currently committed matrix has exactly these 11 coordinates:

```text
net5.0/C# 10
net6.0/C# 10
net7.0/C# 10
net8.0/C# 10
net9.0/C# 10
net10.0/C# 10
net10.0/C# 11
net10.0/C# 12
net10.0/C# 13
net10.0/C# 14
net10.0/C# latest
```

Mark `CorpusProjectDiscoveryTests` `[TestCategory("Unit")]`.

- [ ] **Step 2: Run the discovery tests and verify they fail**

Run:

```powershell
dotnet test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --filter FullyQualifiedName~CorpusProjectDiscoveryTests --nologo
```

Expected: build failure because `CorpusProjectDiscovery` is absent.

- [ ] **Step 3: Implement complete project discovery**

Parse each project as XML and read `TargetFramework`/`TargetFrameworks` and `LangVersion`; do not
infer coordinates only from folder names. Reject missing or contradictory values with the project
path in the message. Return every discovered project; never cap the result set.

- [ ] **Step 4: Add one rebuild test per project**

Run each project sequentially to avoid shared support-project output races:

```text
dotnet build <project> -t:Rebuild --nologo -v:minimal
```

Assert exit code 0 and the literal summaries `0 Warning(s)` and `0 Error(s)`. Use
`[DoNotParallelize]` and `[TestCategory("Integration")]` on the class.

- [ ] **Step 5: Run all 11 project builds**

Run:

```powershell
dotnet test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --filter FullyQualifiedName~CorpusProjectBuildTests --nologo --logger "console;verbosity=detailed"
```

Expected: 11 passing project cases, each reporting 0 warnings and 0 errors.

- [ ] **Step 6: Commit**

```powershell
git add tests/DotNetKnowledge.Corpus.Tests/Projects tests/DotNetKnowledge.Corpus.Tests/CorpusProjectBuildTests.cs
git commit -m "test: enforce the SDK-style corpus build matrix"
```

---

### Task 7: Make Runtime Verification a Corpus Contract

**Files:**
- Create: `tests/DotNetKnowledge.Corpus.Tests/RuntimeClaimCoverageTests.cs`
- Modify: `examples/language-features/CSharp/dotnet/10/latest/library/CSharp11/NumericIntPtr/NumericIntPtr.cs`
- Modify: the five propagated `NumericIntPtr.cs` copies under `examples/language-features/CSharp/dotnet/10/{10.0,11.0,12.0,13.0,14.0}/library/`
- Modify: `AGENTS.md`
- Modify: `CLAUDE.md`
- Modify: `docs/HANDOFF.md`
- Modify: `docs/design/language-feature-showcase-design.md`

**Interfaces:**
- Consumes: source markers in the form `// Runtime verification: <case-id>`
- Consumes: case IDs loaded by `CorpusCaseLoader`
- Produces: a bijection between runtime-verification markers and cases containing one or more runtime expectations

- [ ] **Step 1: Write marker coverage tests**

Scan canonical authored sources only—`latest` for cumulative dotnet projects and the source side of
generated net48 projects. Assert:

1. every `// Runtime verification: X` marker has exactly one case whose ID is `X` and whose
   `runtimes` array is nonempty;
2. every corpus case with a nonempty `runtimes` array has exactly one canonical source marker;
3. duplicate markers fail with all paths listed.

The toolchain-only fixture case is exempt from source markers because its source lives under
`tests/`, not in the corpus.

- [ ] **Step 2: Run the coverage test and verify it fails**

Run:

```powershell
dotnet test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --filter FullyQualifiedName~RuntimeClaimCoverageTests --nologo
```

Expected: failure because `NumericIntPtr.case.json` has runtime evidence but its source lacks the
marker.

- [ ] **Step 3: Add and propagate the NumericIntPtr marker**

Add this line in the explanatory header:

```csharp
// Runtime verification: CSharp11.NumericIntPtr
```

Copy the resulting UTF-8/LF file byte-for-byte to pins 10.0 through 14.0. Verify equality with
`Get-FileHash -Algorithm SHA256`.

- [ ] **Step 4: Replace stale documentation claims**

Update all four documents so they no longer say “the corpus has no test suite and needs none.”
State the new three-layer contract:

```text
1. project builds prove validity at a declared SDK/TFM/language coordinate;
2. isolated compilation cases prove positive and negative feature boundaries;
3. runtime cases prove comments that assert observable behavior.
```

Document that an older TFM selected under SDK 10 uses SDK 10's compiler against the older reference
pack; it does not emulate or select that TFM's historical compiler. Document the required
`// Runtime verification: <case-id>` marker for every new runtime-behavior claim.

- [ ] **Step 5: Run focused tests and documentation guards**

Run:

```powershell
dotnet test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --filter "FullyQualifiedName~RuntimeClaimCoverageTests|FullyQualifiedName~CorpusRuntimeTests" --nologo
dotnet scripts/generate-net48-examples.cs -- --check
dotnet scripts/verify-no-vendored-content.cs
git diff --check
```

Expected: all tests pass; generator reports no drift; vendored-content verifier reports `OK`; Git
reports no whitespace errors.

- [ ] **Step 6: Commit**

```powershell
git add AGENTS.md CLAUDE.md docs/HANDOFF.md docs/design/language-feature-showcase-design.md tests/DotNetKnowledge.Corpus.Tests/RuntimeClaimCoverageTests.cs
git add examples/language-features/CSharp/dotnet/10/10.0/library/CSharp11/NumericIntPtr/NumericIntPtr.cs
git add examples/language-features/CSharp/dotnet/10/11.0/library/CSharp11/NumericIntPtr/NumericIntPtr.cs
git add examples/language-features/CSharp/dotnet/10/12.0/library/CSharp11/NumericIntPtr/NumericIntPtr.cs
git add examples/language-features/CSharp/dotnet/10/13.0/library/CSharp11/NumericIntPtr/NumericIntPtr.cs
git add examples/language-features/CSharp/dotnet/10/14.0/library/CSharp11/NumericIntPtr/NumericIntPtr.cs
git add examples/language-features/CSharp/dotnet/10/latest/library/CSharp11/NumericIntPtr/NumericIntPtr.cs
git commit -m "docs: define executable corpus verification"
```

---

### Task 8: Windows CI and Final Verification

**Files:**
- Create: `.github/workflows/corpus-tests.yml`
- Modify: `README.md`

**Interfaces:**
- Consumes: all tests and required SDK/runtime bands from Tasks 1–7
- Produces: a required, reproducible Windows corpus-verification job

- [ ] **Step 1: Add a Windows workflow with all required toolchains**

Use `windows-latest`, `actions/checkout@v4`, and `actions/setup-dotnet@v4`. Install:

```yaml
dotnet-version: |
  5.0.x
  7.0.x
  10.0.x
```

Restore once, then run:

```powershell
dotnet test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --configuration Release --nologo --logger "trx;LogFileName=corpus-tests.trx"
dotnet scripts/generate-net48-examples.cs -- --check
dotnet scripts/verify-no-vendored-content.cs
```

Upload the TRX file with `actions/upload-artifact@v4` under the name `corpus-test-results`, using
`if: always()` so compiler diagnostics remain available after a failure.

- [ ] **Step 2: Document local prerequisites and commands**

Add a concise README section that says:

- `dotnet test ... --filter "TestCategory=Unit"` needs Windows and only SDK 10;
- the complete suite requires exact SDK versions 5.0.408, 7.0.410, and 10.0.302 plus runtime bands
  5.0, 7.0, and 10.0, and fails preflight if any are absent;
- `dotnet --list-sdks` and `dotnet --list-runtimes` show what is installed;
- targeting `net5.0` under SDK 10 does not select the SDK 5 compiler.

- [ ] **Step 3: Run the complete suite**

Run on a machine with all required toolchains:

```powershell
dotnet test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --configuration Release --nologo --logger "console;verbosity=detailed"
```

Expected:

```text
Failed: 0
Passed: all discovered tests
Skipped: 0
```

Also confirm the output names the exact SDK patch for every compilation coordinate and quotes the
three NumericIntPtr runtime lines.

- [ ] **Step 4: Run repository completion guards**

Run:

```powershell
dotnet scripts/generate-net48-examples.cs -- --check
dotnet scripts/verify-feature-floors.cs
dotnet scripts/verify-no-vendored-content.cs
git diff --check
git status --short
```

Expected:

- generated net48 examples have no drift;
- feature-floor verification has no `MISPLACED` or `NOT-VERSION-SPECIFIC` failure;
- no vendored upstream content;
- no whitespace errors;
- only the intended test, workflow, corpus-marker, and documentation changes remain.

- [ ] **Step 5: Review the final diff**

Run:

```powershell
git diff --stat
git diff -- tests/DotNetKnowledge.Corpus.Tests .github/workflows/corpus-tests.yml AGENTS.md CLAUDE.md README.md docs examples/language-features/CSharp/dotnet/10
```

Confirm that no `.claude/` directory, temporary probe project, `bin/`, `obj/`, `.trx`, or downloaded
reference pack is staged.

- [ ] **Step 6: Commit**

```powershell
git add .github/workflows/corpus-tests.yml README.md
git commit -m "ci: verify corpus compilation and runtime behavior"
```

---

## Follow-up Plan Required: Corpus-Wide Runtime Claim Inventory

Do not claim all 227 corpus rows are runtime-tested after this plan. This plan proves the harness
with the disputed cases and establishes a mechanical marker contract. The next plan must:

1. enumerate each canonical C# and VB group from `MANIFEST.md`;
2. assign exactly one classification: `compile-only`, `runtime-verifiable`, `external-build`, or
   `comments-only`;
3. add an executable case and source marker for every `runtime-verifiable` row;
4. record why `external-build` and `comments-only` rows cannot be executed in the harness;
5. add a coverage test requiring every manifest row to have one classification.

That inventory is a separate reviewable subsystem because it requires row-by-row semantic judgment,
whereas the present plan builds the reusable, test-driven verification machinery.
