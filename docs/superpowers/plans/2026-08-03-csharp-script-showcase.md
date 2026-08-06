# C# Script Showcase Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add eight indexed, BCL-only `.csx` examples verified through Roslyn's embedding API and, where applicable, the matching pinned `csi` host.

**Architecture:** Keep scripts in a host-coordinate tree separate from the project/TFM corpus. A small `net10.0` executable owns strict scenario loading, restricted reference resolution, typed globals, continued submissions, and structured results; corpus tests consume its internal interfaces and exercise the executable end to end. Windows tests restore `Microsoft.Net.Compilers.Toolset` 5.6.0 and run its `tasks/net472/csi.exe`; other platforms keep API verification mandatory and report only `csi` cases Inconclusive.

**Tech Stack:** .NET SDK 10.0.302, C# 14, MSTest 4.3.2, `Microsoft.CodeAnalysis.CSharp.Scripting` 5.6.0, `Microsoft.Net.Compilers.Toolset` 5.6.0, `System.Text.Json`, Roslyn `csi.exe` on Windows.

## Global Constraints

- Authored `.csx` files use only the BCL; no NuGet directives, remote access, third-party runners, notebooks, or vendored binaries.
- Pin `Microsoft.CodeAnalysis.CSharp.Scripting` and `Microsoft.Net.Compilers.Toolset` exactly to `5.6.0`.
- The embedding host targets `net10.0`; corpus tests run through the repository-private .NET SDK `10.0.302` host.
- Keep `.csx` scripts under `examples/language-features/CSharp/csx/roslyn-5.6.0/`, outside the SDK/TFM project-discovery roots.
- `examples/language-features/MANIFEST.md` remains the count of record and must have a one-to-one mapping with scenario descriptors.
- Every host build and successful script compilation has zero errors and zero warnings.
- API cases are mandatory cross-platform. `csi` cases are Windows-conditional and become Inconclusive with an exact prerequisite when the pinned host cannot run.
- Resolve `#load`, input files, and descriptor paths only within their scenario folder. `#r` may resolve only explicitly allowed BCL assemblies.
- The host executes trusted code with process permissions; it is not a sandbox.
- Preserve all unrelated working-tree changes. Use `apply_patch` for authored edits, review `git diff` before every commit, and run `dotnet scripts/verify-no-vendored-content.cs` before completion.

---

## File Structure

Create the host as focused files under `examples/language-features/CSharp/csx/roslyn-5.6.0/host/`:

- `host.csproj` — exact framework/package pins and test internals visibility.
- `Program.cs` — CLI boundary, cancellation/timeout, structured success/error output, exit codes.
- `ScenarioDescriptor.cs` — descriptor records, host enum, and semantic validation.
- `ScenarioDescriptorLoader.cs` — strict JSON deserialization and canonical path validation.
- `ScriptGlobals.cs` — typed globals exposed to API-hosted scripts.
- `RestrictedSourceResolver.cs` — scenario-root-only `#load` resolution.
- `BclMetadataResolver.cs` — allowlisted `#r "System.Xml.Linq"` resolution.
- `ScriptScenarioRunner.cs` — compile-without-warnings, execute, capture output, and continue state.
- `ScriptResult.cs` — structured success and failure payloads.

Create one folder per scenario under `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/`. Every folder owns one `scenario.json`, one entry `.csx`, and only the support files declared by its descriptor.

Add test files under `tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/`:

- `ScenarioDescriptorLoaderTests.cs` — strict schema and path-boundary unit tests.
- `ScriptScenarioRunnerTests.cs` — API runner, warnings, failures, continuation, and cancellation.
- `ScriptHostProcessTests.cs` — executable JSON/exit-code contract and timeout behavior.
- `CSharpScriptApiTests.cs` — dynamic integration cases for every descriptor supporting `api`.
- `CsiToolchain.cs` and `CsiToolchainTests.cs` — exact restored `csi` discovery and version validation.
- `CSharpScriptCsiTests.cs` — dynamic integration cases for every descriptor supporting `csi`.
- `CSharpScriptManifest.cs` and `CSharpScriptManifestTests.cs` — strict parsing of the dedicated manifest table.
- `CSharpScriptCorpusCoverageTests.cs` — manifest/descriptor/file/pin bijection.
- `CSharpScriptTestPaths.cs` — repository-root and showcase-root lookup shared by tests.

All new test files use the namespace `DotNetKnowledge.Corpus.Tests.CSharpScripts`, so the
script-specific verification filter is stable.

Modify:

- `tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj` — host project reference, exact toolset package, and copied `csi` runtime assets.
- `examples/language-features/MANIFEST.md` — eight-row script inventory.
- `AGENTS.md`, `docs/design/language-feature-showcase-design.md`, `docs/design/mcp-tool-surface.md`, and `docs/design/ci.md` — current scripting, query-shape, and pinning truth.

---

### Task 1: Add the strict scenario contract and host scaffold

**Files:**
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/host/host.csproj`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/host/Program.cs`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/host/ScenarioDescriptor.cs`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/host/ScenarioDescriptorLoader.cs`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/host/ScriptResult.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/ScenarioDescriptorLoaderTests.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/CSharpScriptTestPaths.cs`
- Modify: `tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj`

**Interfaces:**
- Consumes: no new feature interfaces.
- Produces: `ScenarioDescriptorLoader.Load(string path)`, `ScenarioDescriptor.Validate(string scenarioDirectory)`, `ScriptHostKind`, `ScenarioExpectation`, `ScriptSuccess`, and `ScriptFailure` for every later task.

- [ ] **Step 1: Create a failing strict-deserialization test**

Add a temporary-file helper to `ScenarioDescriptorLoaderTests` and assert that an unknown JSON member is rejected:

```csharp
[TestMethod]
public void LoadRejectsUnknownMembers()
{
    var path = WriteDescriptor("""
        {
          "id": "sample",
          "entry": "main.csx",
          "supportFiles": [],
          "hosts": ["api"],
          "arguments": [],
          "submissions": [],
          "expectations": {},
          "misspelled": true
        }
        """);

    Assert.ThrowsExactly<JsonException>(() => ScenarioDescriptorLoader.Load(path));
}
```

- [ ] **Step 2: Run the targeted test and verify RED**

Run:

```powershell
$env:DOTNET_HOST_PATH = (Resolve-Path '.artifacts\dotnet\dotnet.exe').Path
& $env:DOTNET_HOST_PATH test 'tests\DotNetKnowledge.Corpus.Tests\DotNetKnowledge.Corpus.Tests.csproj' --filter 'FullyQualifiedName~ScenarioDescriptorLoaderTests' --nologo --no-restore
```

Expected: FAIL because the host project, loader, and descriptor types do not exist.

- [ ] **Step 3: Create the host project and descriptor types**

Use this project shape:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>DotNetKnowledge.CSharpScriptHost</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Scripting" Version="5.6.0" />
    <InternalsVisibleTo Include="DotNetKnowledge.Corpus.Tests" />
  </ItemGroup>
</Project>
```

Define the contract in `ScenarioDescriptor.cs`:

```csharp
internal enum ScriptHostKind { Api, Csi }

internal sealed record ScenarioDescriptor(
    string Id,
    string Entry,
    IReadOnlyList<string> SupportFiles,
    IReadOnlyList<ScriptHostKind> Hosts,
    IReadOnlyList<string> Arguments,
    ScriptGlobalsInput? Globals,
    IReadOnlyList<string> Submissions,
    IReadOnlyDictionary<ScriptHostKind, ScenarioExpectation> Expectations);

internal sealed record ScriptGlobalsInput(string Prefix = "");

internal sealed record ScenarioExpectation(
    int ExitCode,
    string? ReturnType,
    JsonElement? ReturnValue,
    IReadOnlyList<string> StandardOutput,
    IReadOnlyList<string> StandardError,
    int CompletedSubmissionCount);
```

Define the result payloads in `ScriptResult.cs`:

```csharp
internal sealed record ScriptSuccess(
    string ScenarioId,
    string Host,
    string? ReturnType,
    JsonElement ReturnValue,
    IReadOnlyList<string> StandardOutput,
    IReadOnlyList<string> StandardError,
    int CompletedSubmissionCount);

internal sealed record ScriptFailure(
    string Kind,
    string Type,
    string Message,
    IReadOnlyList<string> Diagnostics);
```

Implement `CSharpScriptTestPaths.RepositoryRoot` by walking parents from
`AppContext.BaseDirectory` until `sources.json` exists. Derive `ShowcaseRoot` by appending
`examples/language-features/CSharp/csx/roslyn-5.6.0`, and implement `Descriptor(id)` by appending
`examples/<id>/scenario.json`. Throw `InvalidOperationException` when a required root/file is
absent; never fall back to the current directory.

`Validate` must return all errors in ordinal order and enforce nonblank/unique IDs and hosts, exactly one entry, a matching expectation for each host and no extra expectation, relative in-root paths, existing files, entry/submission `.csx` extensions, and `submissions[0] == entry` when submissions are present. Resolve a path safely with:

```csharp
var fullPath = Path.GetFullPath(Path.Combine(scenarioDirectory, relativePath));
var relative = Path.GetRelativePath(scenarioDirectory, fullPath);
if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
{
    errors.Add($"Path escapes the scenario directory: {relativePath}.");
}
```

- [ ] **Step 4: Implement strict loading**

Use the same strict JSON policy as `CorpusCaseLoader`:

```csharp
private static readonly JsonSerializerOptions SerializerOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};
```

`Load(path)` deserializes, derives `Path.GetDirectoryName(Path.GetFullPath(path))`, calls `Validate`, and throws one `InvalidDataException` containing the descriptor path and every validation error.

- [ ] **Step 5: Add boundary tests and make them pass**

Add explicit tests for blank IDs, duplicate hosts, `../escape.csx`, rooted paths, missing entry, unknown hosts, missing/mismatched expectations, unlisted submissions, and unknown JSON members. Run the targeted filter again.

Expected: PASS, zero warnings.

- [ ] **Step 6: Add the host project reference and restore**

Add to the test project:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\examples\language-features\CSharp\csx\roslyn-5.6.0\host\host.csproj" />
</ItemGroup>
```

Keep `Program.cs` minimal for now: deserialize no scripts and return a structured `not_implemented` failure. Restore through the private host and rerun the loader tests.

- [ ] **Step 7: Commit**

```powershell
git add examples/language-features/CSharp/csx/roslyn-5.6.0/host tests/DotNetKnowledge.Corpus.Tests/CSharpScripts tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj
git commit -m "feat: define C# script scenario contracts"
```

---

### Task 2: Execute expression results and top-level await through the API host

**Files:**
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/host/ScriptGlobals.cs`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/host/ScriptScenarioRunner.cs`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/expression-result/main.csx`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/expression-result/scenario.json`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/top-level-await/main.csx`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/top-level-await/scenario.json`
- Create: `tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/ScriptScenarioRunnerTests.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/ScriptHostProcessTests.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/CSharpScriptApiTests.cs`
- Modify: `examples/language-features/CSharp/csx/roslyn-5.6.0/host/Program.cs`

**Interfaces:**
- Consumes: Task 1's descriptor contract and loader.
- Produces: `ScriptScenarioRunner.RunAsync(ScenarioDescriptor descriptor, string scenarioDirectory, CancellationToken cancellationToken)` returning `ScriptSuccess`; the executable contract `dotnet host.dll <scenario.json>` with exit codes 0 success, 1 validation/compile/runtime failure, 2 cancellation, and 3 timeout.

- [ ] **Step 1: Write a failing return-value test**

```csharp
[TestMethod]
public async Task RunAsyncCapturesTypedFinalExpression()
{
    var descriptorPath = CSharpScriptTestPaths.Descriptor("expression-result");
    var descriptor = ScenarioDescriptorLoader.Load(descriptorPath);

    var result = await new ScriptScenarioRunner().RunAsync(
        descriptor,
        Path.GetDirectoryName(descriptorPath)!,
        TestContext.CancellationToken);

    Assert.AreEqual("System.Int32", result.ReturnType);
    Assert.AreEqual("42", result.ReturnValue.GetRawText());
    CollectionAssert.AreEqual(Array.Empty<string>(), result.StandardOutput.ToArray());
}
```

Run the `ScriptScenarioRunnerTests` filter. Expected: FAIL because the runner and scenario do not exist.

- [ ] **Step 2: Add the two canonical scripts and descriptors**

`expression-result/main.csx`:

```csharp
int Twice(int value) => value * 2;

Twice(21)
```

`top-level-await/main.csx`:

```csharp
await System.Threading.Tasks.Task.Yield();
System.Console.WriteLine("resumed after await");
```

Both descriptors name `api` and `csi`. The API expectation for `expression-result` has return type `System.Int32`, return value `42`, and no output. The await case has a null return and one output line, `resumed after await`. Leave the `csi` expectation present for Task 6; it expects exit 0 and the host-specific output actually produced by batch `csi`.

- [ ] **Step 3: Implement the minimal API runner**

Define the final globals shape now so later scenario tasks only add authored examples:

```csharp
internal sealed class ScriptGlobals(
    string[] args,
    string prefix,
    CancellationToken cancellationToken)
{
    public string[] Args { get; } = args;
    public CancellationToken CancellationToken { get; } = cancellationToken;
    public string Prefix { get; } = prefix;
    public string Format(string value) => $"{Prefix}: {value}";
}
```

The runner reads the entry file and builds options with explicit host BCL references before adding
the entry path:

```csharp
var options = ScriptOptions.Default
    .AddReferences(
        typeof(Console).Assembly,
        typeof(Enumerable).Assembly,
        typeof(System.Text.Json.JsonDocument).Assembly,
        typeof(System.Xml.Linq.XDocument).Assembly)
    .WithFilePath(entryPath);
```

Redirect `Console.Out` and `Console.Error` to invariant `StringWriter` instances for the duration of
one run, create `CSharpScript.Create<object?>(code, options, typeof(ScriptGlobals))`, and call
`Compile()` before execution. Reject every warning or error:

```csharp
var diagnostics = script.Compile()
    .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
    .ToArray();
if (diagnostics.Length != 0)
{
    throw new CompilationErrorException(
        "Script compilation produced diagnostics.",
        diagnostics.ToImmutableArray());
}
```

Execute with `RunAsync`, serialize the return value with `JsonSerializer.SerializeToElement`, use `ReturnValue?.GetType().FullName`, normalize CRLF to LF, and split captured output into lines without inventing a blank terminal line. Always restore the original console writers in `finally`.

- [ ] **Step 4: Implement the CLI result boundary**

`Program.cs` requires exactly one descriptor path. Use a linked cancellation token combining `Console.CancelKeyPress` and a 30-second timeout. Serialize `ScriptSuccess` to standard output. On `CompilationErrorException`, emit diagnostic IDs/messages in `ScriptFailure`; on other exceptions emit the original type/message to standard error. Distinguish cancellation from timeout with exit codes 2 and 3.

- [ ] **Step 5: Test warnings, runtime failures, cancellation, and process JSON**

Use temporary scenario folders in unit tests. Add scripts containing `int unused;` (expect warning rejection), `throw new InvalidOperationException("boom");` (expect original type/message), and `await Task.Delay(Timeout.Infinite, CancellationToken);` through a test globals token (expect cancellation). In `ScriptHostProcessTests`, launch the built host with `ProcessRunner`, parse its one-line JSON, and assert no success JSON appears after a failure.

- [ ] **Step 6: Add API dynamic data and run GREEN**

`CSharpScriptApiTests` enumerates every descriptor whose `Hosts` contains `Api`, runs it, and compares exit code, return type/value, normalized output, and standard error to `ScenarioExpectation`. Mark the class `[DoNotParallelize]` because console capture is process-global.

Run the runner, process, and API test filters. Expected: all pass with zero warnings.

- [ ] **Step 7: Commit**

```powershell
git add examples/language-features/CSharp/csx/roslyn-5.6.0 tests/DotNetKnowledge.Corpus.Tests/CSharpScripts
git commit -m "feat: execute C# scripts through Roslyn"
```

---

### Task 3: Restrict and demonstrate `#load` and `#r`

**Files:**
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/host/RestrictedSourceResolver.cs`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/host/BclMetadataResolver.cs`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/load-relative-script/main.csx`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/load-relative-script/shared.csx`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/load-relative-script/scenario.json`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/reference-bcl-assembly/main.csx`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/reference-bcl-assembly/scenario.json`
- Modify: `examples/language-features/CSharp/csx/roslyn-5.6.0/host/ScriptScenarioRunner.cs`
- Modify: `tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/ScriptScenarioRunnerTests.cs`

**Interfaces:**
- Consumes: Task 2's runner.
- Produces: scenario-root-only `SourceReferenceResolver` and an allowlisted `MetadataReferenceResolver` mapping `System.Xml.Linq` to `typeof(XDocument).Assembly.Location`.

- [ ] **Step 1: Write failing resolver tests**

Test that `#load "shared.csx"` works when `Environment.CurrentDirectory` is outside the scenario, while `#load "../outside.csx"` fails even when the outside file exists. Test that `#r "System.Xml.Linq"` succeeds and `#r "unapproved.dll"` fails with an unresolved-reference diagnostic.

Run the runner test filter. Expected: the relative load and allowlisted assembly cases fail before resolver implementation.

- [ ] **Step 2: Add canonical directive scenarios**

`load-relative-script/shared.csx`:

```csharp
string Describe(int value) => $"loaded value: {value}";
```

`load-relative-script/main.csx`:

```csharp
#load "shared.csx"

System.Console.WriteLine(Describe(42));
```

`reference-bcl-assembly/main.csx`:

```csharp
#r "System.Xml.Linq"

using System.Xml.Linq;

System.Console.WriteLine(XDocument.Parse("<root />").Root!.Name.LocalName);
```

Both descriptors support `api` and `csi`, with output `loaded value: 42` and `root` respectively.

- [ ] **Step 3: Implement restricted source resolution**

Wrap `SourceFileResolver` and override `NormalizePath`, `ResolveReference`, and `OpenRead`. Canonicalize every resolved file and reject it unless `Path.GetRelativePath(root, candidate)` remains non-rooted and does not equal/start with `..`. `Equals` compares the canonical root ordinally on case-sensitive platforms and ordinal-ignore-case on Windows; `GetHashCode` uses the matching comparer.

- [ ] **Step 4: Implement the BCL metadata allowlist**

`BclMetadataResolver.ResolveReference` accepts only the exact assembly name `System.Xml.Linq` and returns `MetadataReference.CreateFromFile(typeof(XDocument).Assembly.Location, properties)`. Return an empty immutable array for every other reference. Its equality/hash code are value-based so Roslyn can cache compilations correctly.

- [ ] **Step 5: Wire resolvers and run GREEN**

Build options per scenario:

```csharp
var options = ScriptOptions.Default
    .WithFilePath(entryPath)
    .WithSourceResolver(new RestrictedSourceResolver(scenarioDirectory))
    .WithMetadataResolver(new BclMetadataResolver());
```

Run the runner and API integration filters from a working directory outside each scenario. Expected: four API scenarios pass; escape and unapproved-reference fixtures fail with explicit diagnostics.

- [ ] **Step 6: Commit**

```powershell
git add examples/language-features/CSharp/csx/roslyn-5.6.0
git add tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/ScriptScenarioRunnerTests.cs
git commit -m "feat: demonstrate restricted script directives"
```

---

### Task 4: Add command arguments and typed globals

**Files:**
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/command-line-arguments/main.csx`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/command-line-arguments/scenario.json`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/typed-globals/main.csx`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/typed-globals/scenario.json`
- Modify: `tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/ScriptScenarioRunnerTests.cs`

**Interfaces:**
- Consumes: Task 2's `ScriptGlobalsInput`, final `ScriptGlobals`, and runner.
- Produces: two authored examples proving the existing globals contract.

- [ ] **Step 1: Write failing globals tests**

Assert that arguments `alpha` and `two words` are visible as `Args`, and that a descriptor global prefix `agent` makes `Format("ready")` return `agent: ready`. Add a negative test proving the typed-globals descriptor does not claim `csi` support.

- [ ] **Step 2: Add the scripts and descriptors**

`command-line-arguments/main.csx`:

```csharp
System.Console.WriteLine(string.Join("|", Args));
```

Its descriptor supports both hosts and supplies `alpha`, `two words`; expected output is `alpha|two words`.

`typed-globals/main.csx`:

```csharp
System.Console.WriteLine(Format("ready"));
```

Its descriptor supports only `api`, sets `globals.prefix` to `agent`, and expects `agent: ready`.

- [ ] **Step 3: Run the scenarios through the existing globals contract**

The Task 2 runner already constructs `ScriptGlobals` from `descriptor.Arguments`,
`descriptor.Globals?.Prefix ?? string.Empty`, and the execution cancellation token. Run targeted
runner and API tests. Expected: six API scenarios pass.

- [ ] **Step 4: Commit**

```powershell
git add examples/language-features/CSharp/csx/roslyn-5.6.0
git add tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/ScriptScenarioRunnerTests.cs
git commit -m "feat: demonstrate script arguments and globals"
```

---

### Task 5: Add continued submissions and a practical JSON transformation

**Files:**
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/continued-submissions/initialize.csx`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/continued-submissions/continue.csx`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/continued-submissions/scenario.json`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/json-file-transform/main.csx`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/json-file-transform/input.json`
- Create: `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/json-file-transform/scenario.json`
- Modify: `examples/language-features/CSharp/csx/roslyn-5.6.0/host/ScriptScenarioRunner.cs`
- Modify: `tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/ScriptScenarioRunnerTests.cs`

**Interfaces:**
- Consumes: Task 4's globals and Task 2's success payload.
- Produces: ordered descriptor submissions and `CompletedSubmissionCount`; descriptor arguments that equal a listed support file are canonicalized to an absolute in-root path before being exposed as `Args`.

- [ ] **Step 1: Write failing continuation and file-argument tests**

Assert that a second submission sees a variable and function declared by the first, returns `5`, and reports two completed submissions. Assert that the JSON script succeeds when launched outside its scenario directory because its `input.json` argument is canonicalized from `supportFiles`.

- [ ] **Step 2: Add continued-submission scripts**

`initialize.csx`:

```csharp
var count = 2;
int Add(int left, int right) => left + right;
```

`continue.csx`:

```csharp
count = Add(count, 3);
System.Console.WriteLine(count);
count
```

The descriptor is API-only, sets `entry` to `initialize.csx`, lists `continue.csx` as support, orders both in `submissions`, and expects return value `5`, output `5`, and completed count 2.

- [ ] **Step 3: Preserve `ScriptState` safely**

For submission 1, compile and run as Task 2 does. For every later file, use `state.Script.ContinueWith<object?>(code, options.WithFilePath(path))`, call `Compile()` and reject warnings/errors before `RunFromAsync(state, cancellationToken)`. The final state's return value is the scenario result. Never execute a continuation that has diagnostics.

- [ ] **Step 4: Add the JSON transformation**

Use this deterministic input:

```json
{"items":[{"name":"beta","enabled":false},{"name":"alpha","enabled":true},{"name":"gamma","enabled":true}]}
```

The API-only script reads `Args[0]`, selects enabled names, sorts them ordinally, and emits compact JSON:

```csharp
using System.Linq;
using System.Text.Json;

using var document = JsonDocument.Parse(System.IO.File.ReadAllText(Args[0]));
var names = document.RootElement.GetProperty("items")
    .EnumerateArray()
    .Where(item => item.GetProperty("enabled").GetBoolean())
    .Select(item => item.GetProperty("name").GetString()!)
    .OrderBy(name => name, System.StringComparer.Ordinal)
    .ToArray();

System.Console.WriteLine(JsonSerializer.Serialize(new { enabledNames = names }));
```

Expected output: `{"enabledNames":["alpha","gamma"]}`.

- [ ] **Step 5: Canonicalize declared file arguments and run GREEN**

When an argument exactly matches a `supportFiles` entry, replace it with the already validated absolute path. Leave ordinary arguments unchanged. Run runner and API tests. Expected: all eight API scenarios pass, with the two host-only cases excluded from future `csi` enumeration.

- [ ] **Step 6: Commit**

```powershell
git add examples/language-features/CSharp/csx/roslyn-5.6.0
git add tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/ScriptScenarioRunnerTests.cs
git commit -m "feat: demonstrate stateful and practical C# scripts"
```

---

### Task 6: Verify shared scenarios through pinned `csi`

**Files:**
- Create: `tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/CsiToolchain.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/CsiToolchainTests.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/CSharpScriptCsiTests.cs`
- Modify: `tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj`
- Modify: applicable `examples/language-features/CSharp/csx/roslyn-5.6.0/examples/*/scenario.json`

**Interfaces:**
- Consumes: Task 1 descriptors and the existing `ProcessRunner`.
- Produces: `CsiToolchain.TryResolve(string baseDirectory, out string path, out string reason)` and dynamic `csi` parity tests.

- [ ] **Step 1: Write failing toolchain tests**

Test these cases independently: non-Windows returns an unsupported-platform reason; missing `Csi/csi.exe` names the exact path; a mismatched `FileVersionInfo.ProductVersion` names expected `5.6.0`; a valid executable resolves. Abstract OS/version reads behind constructor delegates so unit tests do not depend on the developer machine.

- [ ] **Step 2: Add the exact toolset package and runtime assets**

Add to the test project:

```xml
<PackageReference Include="Microsoft.Net.Compilers.Toolset"
                  Version="5.6.0"
                  PrivateAssets="all"
                  GeneratePathProperty="true" />
```

Copy only top-level net472 toolset files into test output; these are restored build artifacts, not tracked content:

```xml
<Content Include="$(PkgMicrosoft_Net_Compilers_Toolset)\tasks\net472\*.*"
         Link="Csi\%(Filename)%(Extension)"
         CopyToOutputDirectory="PreserveNewest" />
```

The package contains `tasks/net472/csi.exe`; do not search `PATH` or Visual Studio.

Restore the test project through the private host after changing the package graph:

```powershell
$env:DOTNET_HOST_PATH = (Resolve-Path '.artifacts\dotnet\dotnet.exe').Path
& $env:DOTNET_HOST_PATH restore 'tests\DotNetKnowledge.Corpus.Tests\DotNetKnowledge.Corpus.Tests.csproj' --nologo
```

- [ ] **Step 3: Implement exact version validation**

On Windows, resolve `Path.Combine(AppContext.BaseDirectory, "Csi", "csi.exe")`, require the file, and require `FileVersionInfo.GetVersionInfo(path).ProductVersion` to begin with `5.6.0` followed by end-of-string or a non-digit separator. Return a precise reason instead of throwing for an unavailable optional host.

- [ ] **Step 4: Add dynamic `csi` execution tests**

Enumerate descriptors containing `Csi`. If resolution fails, call `Assert.Inconclusive(reason)`. Otherwise run:

```csharp
var arguments = new List<string> { "/nologo", entryPath };
arguments.AddRange(resolvedScenarioArguments);
var result = await runner.RunAsync(csiPath, arguments, workingDirectory: repositoryRoot,
    cancellationToken: TestContext.CancellationToken);
```

Compare exit code and normalized standard output/error to the `csi` expectation. Include only `expression-result`, `top-level-await`, `load-relative-script`, `reference-bcl-assembly`, and `command-line-arguments`; typed globals, continued submissions, and `System.Text.Json` remain API-only because this pinned `csi` is a net472 executable.

- [ ] **Step 5: Run RED, correct observed batch-host expectations, then run GREEN**

Run the `CsiToolchainTests` and `CSharpScriptCsiTests` filters. The first execution is the measurement for batch-host presentation (especially whether a trailing expression is printed). Update only the per-host expected output in descriptors; do not change shared script behavior to force API and `csi` presentation to match.

Expected on Windows: five passing `csi` cases. Expected elsewhere: unit tests pass and five integration cases report Inconclusive with the net472/Windows prerequisite.

- [ ] **Step 6: Commit**

```powershell
git add tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj
git add tests/DotNetKnowledge.Corpus.Tests/CSharpScripts
git add examples/language-features/CSharp/csx/roslyn-5.6.0/examples
git commit -m "test: verify C# scripts through pinned csi"
```

---

### Task 7: Make the manifest the enforced script inventory

**Files:**
- Create: `tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/CSharpScriptManifest.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/CSharpScriptManifestTests.cs`
- Create: `tests/DotNetKnowledge.Corpus.Tests/CSharpScripts/CSharpScriptCorpusCoverageTests.cs`
- Modify: `examples/language-features/MANIFEST.md`
- Modify: `AGENTS.md`
- Modify: `docs/design/language-feature-showcase-design.md`
- Modify: `docs/design/mcp-tool-surface.md`
- Modify: `docs/design/ci.md`

**Interfaces:**
- Consumes: all eight descriptors, exact host/test package pins, and Task 6 host declarations.
- Produces: `CSharpScriptManifest.Load(string manifestPath)` returning `ScriptManifestRow(Id, Entry, Hosts, Demonstrates, Note)` and a coverage gate for the complete showcase.

- [ ] **Step 1: Write failing strict manifest tests**

Use temporary Markdown fixtures to require the exact heading and header:

```markdown
## C# scripts (`.csx`)

| Scenario | Entry | Hosts | Demonstrates | Note |
|---|---|---|---|---|
```

Test duplicate IDs, malformed column counts, unknown hosts, missing table, and content after the table before the next `##` heading. Expected before implementation: FAIL because the parser does not exist.

- [ ] **Step 2: Implement the focused parser**

Read only lines between the exact script heading and the next level-two heading. Require the exact five-column header and separator. Split rows on `|`, trim whitespace/backticks, parse comma-separated hosts through `ScriptHostKind`, and reject blank fields except `Note`. Report manifest line numbers in every error. Do not create a general Markdown parser.

- [ ] **Step 3: Add the eight manifest rows**

Add one row for each approved scenario. Use repository-relative entries beneath `CSharp/csx/roslyn-5.6.0/examples/`, list `api, csi` or `api` exactly, and state the host distinction for command arguments, typed globals, continued submissions, and the net10-only JSON API host.

- [ ] **Step 4: Add the coverage test**

Prove exact equality between manifest IDs and descriptor IDs. For every scenario directory, compare actual files recursively with exactly `scenario.json`, `entry`, and `supportFiles`. Reject orphan `.csx` files, undeclared inputs, duplicate canonical paths, path escapes, and mismatched entry paths/hosts.

Parse the host and test csproj files with `XDocument` and assert both Roslyn package versions are `5.6.0`; assert the directory segment is `roslyn-5.6.0`; assert the Toolset reference has `PrivateAssets=all`; and rerun `CorpusProjectDiscoveryTests` to prove the new host did not enter the SDK/TFM matrix.

- [ ] **Step 5: Update current-truth documentation**

- `AGENTS.md`: add the `CSharp/csx/roslyn-5.6.0` layout, BCL-only rule, API/csi verification, trust warning, and explicit distinction from .NET 10 file-based programs.
- `docs/design/language-feature-showcase-design.md`: add script-host coordinates beside project coordinates and extend the verification contract without rewriting its historical compiler analysis.
- `docs/design/mcp-tool-surface.md`: define `list_examples(kind?, language?, version?, feature?)` and make `get_example` return `kind: "script"`, Roslyn host/version, entry/support files, applicable hosts, and descriptor-backed behavior.
- `docs/design/ci.md`: explain the exact Roslyn 5.6.0 API/toolset pair and that the restored net472 `csi` assets are Windows-only build output, never vendored content.

- [ ] **Step 6: Run the inventory and documentation gates**

Run:

```powershell
$env:DOTNET_HOST_PATH = (Resolve-Path '.artifacts\dotnet\dotnet.exe').Path
& $env:DOTNET_HOST_PATH test 'tests\DotNetKnowledge.Corpus.Tests\DotNetKnowledge.Corpus.Tests.csproj' --filter 'FullyQualifiedName~CSharpScriptManifestTests|FullyQualifiedName~CSharpScriptCorpusCoverageTests|FullyQualifiedName~CorpusProjectDiscoveryTests' --nologo --no-restore
dotnet scripts/verify-no-vendored-content.cs
git diff --check
```

Expected: all selected tests pass, no vendored findings, and no whitespace errors.

- [ ] **Step 7: Commit**

```powershell
git add examples/language-features/MANIFEST.md AGENTS.md
git add docs/design/language-feature-showcase-design.md docs/design/mcp-tool-surface.md docs/design/ci.md
git add tests/DotNetKnowledge.Corpus.Tests/CSharpScripts
git commit -m "docs: index the verified C# script showcase"
```

---

### Task 8: Run the complete verification contract

**Files:**
- Modify only files required to correct failures introduced by Tasks 1-7; do not broaden scope.

**Interfaces:**
- Consumes: the complete showcase and every repository verification layer.
- Produces: fresh evidence that the feature and existing corpus remain valid.

- [ ] **Step 1: Verify exact SDK prerequisites**

```powershell
dotnet scripts/install-corpus-test-sdks.cs -- --check
```

Expected: exact SDKs 5.0.408, 7.0.410, and 10.0.302 are present. If absent, run `dotnet scripts/install-corpus-test-sdks.cs` before continuing.

- [ ] **Step 2: Restore and build the host directly**

```powershell
$env:DOTNET_HOST_PATH = (Resolve-Path '.artifacts\dotnet\dotnet.exe').Path
$env:NUGET_PACKAGES = 'C:\Users\patri\.nuget\packages'
& $env:DOTNET_HOST_PATH restore 'tests\DotNetKnowledge.Corpus.Tests\DotNetKnowledge.Corpus.Tests.csproj' --nologo
& $env:DOTNET_HOST_PATH build 'examples\language-features\CSharp\csx\roslyn-5.6.0\host\host.csproj' --configuration Release --nologo --no-restore
```

Expected: 0 warnings and 0 errors.

- [ ] **Step 3: Run script-specific tests with detailed output**

```powershell
& $env:DOTNET_HOST_PATH test 'tests\DotNetKnowledge.Corpus.Tests\DotNetKnowledge.Corpus.Tests.csproj' --configuration Release --filter 'FullyQualifiedName~CSharpScripts' --nologo --no-restore --logger 'console;verbosity=detailed'
```

Expected on Windows with the restored package: all descriptor, host, API, manifest, coverage, toolchain, and five `csi` cases pass; no silent skips.

- [ ] **Step 4: Run the full corpus suite**

```powershell
& $env:DOTNET_HOST_PATH test 'tests\DotNetKnowledge.Corpus.Tests\DotNetKnowledge.Corpus.Tests.csproj' --configuration Release --nologo --no-restore --logger 'console;verbosity=normal'
```

Expected: all tests pass, except existing explicitly Inconclusive tests for genuinely absent external runtimes/toolchains.

- [ ] **Step 5: Run repository guards and inspect scope**

```powershell
dotnet scripts/verify-no-vendored-content.cs
dotnet scripts/verify-feature-floors.cs
git diff --check
git status --short
git diff --stat
```

Expected: no vendored content, no failing floor verdict, no whitespace errors, only intended showcase/test/doc changes, and no tracked toolset binaries.

- [ ] **Step 6: Commit any verification-only corrections**

If verification required corrections, stage only those exact files and commit:

```powershell
git commit -m "fix: satisfy C# script verification contract"
```

If no corrections were needed, do not create an empty commit.

---

## Definition of Done

1. All eight scenario folders exist, validate, and have exactly one manifest row.
2. The host and tests pin Roslyn 5.6.0 exactly and build at zero warnings/errors on SDK 10.0.302.
3. All eight API cases pass cross-platform; the five shared cases pass through the restored `tasks/net472/csi.exe` on Windows.
4. Missing `csi` support produces a precise Inconclusive result only for `csi` cases.
5. Strict descriptor, path traversal, unknown-field, diagnostics, exception, cancellation, and timeout tests pass.
6. Coverage rejects orphan scripts/files, manifest drift, path escapes, host mismatches, and Roslyn pin mismatches.
7. Existing project discovery and the full corpus test suite remain green.
8. `verify-no-vendored-content`, `verify-feature-floors`, and `git diff --check` are clean.
9. Documentation distinguishes Roslyn `.csx` scripts from .NET 10 file-based `.cs` programs and records that scripts execute trusted code with process permissions.
