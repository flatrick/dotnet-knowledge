# C# Script Showcase Design

## Purpose

Add trustworthy examples of Roslyn C# scripts (`.csx`). The showcase teaches the execution model
that makes a script different from a compiled C# project and includes practical, self-contained
uses of that model. It is not the .NET 10 file-based program feature and does not duplicate the
existing language-version corpus.

The first version uses only the BCL in authored scripts. It pins Roslyn 5.6.0 and verifies scripts
through `Microsoft.CodeAnalysis.CSharp.Scripting` on every supported platform and through the
matching `csi` distribution where that host is available.

## Scope

The initial showcase covers:

- declarations followed by a typed final-expression result;
- top-level `await` without an application entry point;
- relative composition with `#load`;
- a BCL assembly reference with `#r`;
- command-line arguments;
- strongly typed globals supplied by an embedding host;
- state shared across continued submissions; and
- a deterministic, BCL-only JSON file transformation.

It excludes NuGet directives, network access, notebooks, third-party script runners, untrusted-code
sandboxing, a historical Roslyn matrix, and script-form copies of ordinary C# language features.

## Corpus Placement

The showcase lives at:

```text
examples/language-features/CSharp/csx/roslyn-5.6.0/
  examples/
    <scenario-id>/
      scenario.json
      <entry>.csx
      <optional-support>.csx
      <optional-input>
  host/
    host.csproj
    Program.cs
    ... focused host components
```

This path describes a script-host coordinate. It does not belong under `CSharp/dotnet/`, whose
paths describe SDK, TFM, and `LangVersion` project coordinates. Consequently, existing SDK-style
project discovery remains unchanged.

`examples/language-features/MANIFEST.md` gains a distinct **C# scripts (`.csx`)** section. Each row
names a stable scenario ID, its entry file, applicable hosts, the behavior it demonstrates, and any
host-specific distinction. The manifest remains the count of record.

The existing `CSharp/dotnet/10/file-based/` examples remain unchanged. Corpus documentation states
explicitly that those `.cs` files use the .NET 10 file-based program model, while this showcase uses
Roslyn's `.csx` scripting model. A future MCP example index represents this distinction as a
structured example kind, such as `project` or `script`, rather than asking callers to infer it from
a path.

## Scenario Catalog

### `expression-result`

Declares values and functions at script scope, then ends with an expression. The API host captures
the expression's value and runtime type. `csi` executes the same canonical script, but its batch
output is asserted according to `csi` behavior rather than being required to serialize the API
return value.

### `top-level-await`

Awaits BCL asynchronous work directly at script scope. The script has no `Main` method and produces
deterministic output.

### `load-relative-script`

Uses `#load` to import one support `.csx` file from the same scenario folder. The loaded script
contributes a declaration used by the entry script. Verification starts outside the scenario
directory so success proves that resolution is based on the entry script's configured path, not the
process working directory.

### `reference-bcl-assembly`

Uses `#r` to reference a framework assembly and then consumes a type from that assembly. The exact
reference must resolve in both configured Roslyn hosts without a package download or machine-local
path. The scenario descriptor records parity for both hosts.

### `command-line-arguments`

Consumes a deterministic argument list. `csi` supplies its normal script arguments; the API host
supplies an equivalent `Args` member through typed globals. The manifest identifies this as a host
equivalence rather than a language guarantee.

### `typed-globals`

Reads values and invokes a BCL-backed service from a strongly typed globals object supplied by the
embedding host. This scenario is API-host-specific because plain `csi` does not instantiate the
custom globals type.

### `continued-submissions`

Runs two script submissions through one API-host session. The second submission reads and mutates
state and calls a declaration created by the first. This scenario is API-host-specific; automated
batch invocation of separate `csi` files is not treated as an equivalent persistent REPL session.

### `json-file-transform`

Reads a repository-local JSON input with `System.Text.Json`, projects it into a deterministic shape,
and emits normalized JSON. It demonstrates a useful script without network access, external
packages, or writes outside the test's temporary directory.

## Scenario Descriptors

Every scenario folder contains one `scenario.json`. The descriptor is the machine-readable
execution contract and contains:

- `id`: the stable manifest ID;
- `entry`: the canonical entry `.csx` path relative to the scenario folder;
- `supportFiles`: every additional authored `.csx` or input file owned by the scenario;
- `hosts`: one or both of `api` and `csi`;
- `arguments`: the deterministic argument list;
- `globals`: typed-global input values where applicable;
- `submissions`: the ordered files for a continued-submission case; and
- per-host expected return type, return value, standard output, standard error, and exit code.

Fields that do not apply to a scenario are omitted. The descriptor schema rejects unknown fields so
misspellings cannot silently weaken verification. Paths must be relative, remain inside the
scenario folder after canonicalization, and refer to files listed by the descriptor.

The manifest and descriptors deliberately serve different purposes: the manifest is the corpus
inventory and future query index; descriptors are executable assertions. A coverage test enforces
a bijection between them so neither can drift independently.

## Embedding Host

The example host is a small `net10.0` executable with an exact package reference to
`Microsoft.CodeAnalysis.CSharp.Scripting` 5.6.0. It executes canonical `.csx` files from the corpus;
it does not copy script source into C# string literals.

The host has focused components for:

- loading and validating a scenario descriptor;
- constructing `ScriptOptions` with explicit BCL metadata references;
- setting the entry file path and a source resolver rooted at the scenario directory;
- binding a typed globals object when requested;
- retaining `ScriptState` across declared submissions;
- capturing script output independently of the host's result channel; and
- serializing a compact outcome.

A successful run writes one JSON object containing the scenario ID, host, return type, return value,
captured standard output, and completed-submission count. This structured output keeps the example
useful to an agent caller and gives tests an unambiguous assertion surface.

The host accepts cancellation and enforces a bounded execution timeout. It is an execution host,
not a security boundary. Documentation warns that a Roslyn script has the process's permissions and
must be trusted.

## `csi` Host

Applicable scenarios run from their canonical entry paths using the `csi` distribution matching
Roslyn 5.6.0. The verifier supplies the descriptor's arguments and captures process output and exit
code. Assertions remain per host: shared script behavior must agree, while presentation differences
such as API return-value capture are recorded rather than normalized away.

Verification never discovers an arbitrary `csi` from `PATH` or silently uses Visual Studio's copy.
It resolves the expected version from the pinned Roslyn toolset restored for the test environment
and validates that version before execution. If the compatible executable cannot run on the current
platform, only the `csi` cases are Inconclusive and the message names the exact prerequisite. API
verification remains mandatory and cross-platform.

## Failure Behavior

- Unexpected Roslyn diagnostics fail compilation verification. Warnings are failures, matching the
  rest of the corpus.
- Script compilation failures expose their diagnostic IDs and messages.
- Runtime exceptions retain their original type and message and make the host exit nonzero.
- Missing or escaping `#load` paths and unresolved `#r` references fail visibly. Resolution does not
  fall back to unrelated working-tree or machine directories.
- Invalid descriptors fail before script execution with the descriptor path and offending field.
- Cancellation and timeout are distinct outcomes; a timeout terminates process-based `csi`
  execution and cancels API-host execution.
- Missing optional `csi` prerequisites produce an explicit Inconclusive result, never a passing
  test or silent omission.

## Verification Strategy

Verification has four layers.

### Host build

Build the embedding host at zero errors and zero warnings with the pinned .NET 10 SDK and exact
Roslyn package version.

### Manifest and descriptor coverage

A coverage test proves:

- every scripting manifest row has exactly one descriptor;
- every descriptor has exactly one manifest row;
- IDs and entry paths are unique;
- every authored file below an example folder belongs to that descriptor;
- no `.csx` file is orphaned or shared between scenario folders;
- all referenced paths exist and remain within the scenario folder; and
- the directory, host project, test project, and `csi` toolset agree on Roslyn 5.6.0.

### API execution

Every `api` scenario compiles and runs through the embedding host. Tests reject unexpected
diagnostics and compare the host's structured result with the descriptor. These tests are mandatory
on every platform supported by the test project.

### `csi` execution

Every `csi` scenario runs through the matching pinned executable when the platform supports it.
Tests compare normalized output, standard error, and exit code with the descriptor. Missing host
support is reported as Inconclusive with a remediation message.

The verifier also has isolated tests for malformed descriptors, path traversal, missing `#load`
targets, unresolved references, compilation errors, runtime exceptions, cancellation, and timeout.
These fixtures live under the test project rather than in the authored showcase, because they test
the harness rather than demonstrate useful scripting behavior.

## Package and Content Policy

Roslyn dependencies are exact package references, not vendored binaries. Authored `.csx`, JSON, and
C# host files are repository-owned content. Scripts make no network requests and resolve no NuGet
packages. The existing no-vendored-content verification remains part of the completion checks.

Official package coordinates:

- `Microsoft.CodeAnalysis.CSharp.Scripting` 5.6.0
- `Microsoft.Net.Compilers.Toolset` 5.6.0 for the matching `csi` distribution

## Completion Criteria

The feature is complete when all eight scenarios are present and indexed, the host builds without
warnings, all API cases pass, applicable `csi` cases pass or report a precise missing prerequisite,
coverage proves there are no unindexed scripting files, existing corpus verification remains green,
and repository documentation clearly distinguishes `.csx` scripts from .NET 10 file-based programs.
