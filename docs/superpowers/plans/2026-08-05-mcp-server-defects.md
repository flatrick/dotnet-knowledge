# MCP Server Defect Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the MCP server usable from an MCP client, and correct three defects in its
API-documentation query surface.

**Architecture:** Six independent changes to `src/DotNetKnowledge.Mcp/`, sequenced so the blocker
lands first. `GitCommandRunner` gains a redirected standard input and per-command timeout tiers;
`ApiDocsQueryService` gains generic-member matching, non-shadowing type resolution, and a paginated
two-detail-level response; `sync_source` gains progress notifications; the SDK moves to 2.0.0.

**Tech Stack:** .NET 10, C#, MSTest (`MSTest.Sdk/4.3.2`), `ModelContextProtocol` (1.3.0 → 2.0.0),
Git for Windows.

**Spec:** [`docs/superpowers/specs/2026-08-05-mcp-server-defects-design.md`](../specs/2026-08-05-mcp-server-defects-design.md)

**Branch:** `mcp-server-defects`, already checked out with the spec committed.

## Global Constraints

Every task's requirements implicitly include this section.

- **0 errors and 0 warnings.** `TreatWarningsAsErrors` is inherited from the root
  `Directory.Build.props`. Never add `#pragma warning disable`; never override the property.
- **stdout is the MCP protocol channel.** Every log goes to stderr. Do not add a console logging
  provider that writes to stdout.
- **Every payload carries the provenance envelope** — `repo`, `ref`, `commit`, `fetchedAt`. Never
  absent, never inferred.
- **No silent truncation.** Every capped result set carries `isPartial` or a cursor.
- **Search tools return names and locations, never bodies.**
- **American English** for identifiers, comments, and prose.
- **LF line endings, UTF-8.**
- **Tooling is single-file C#, never a shell script.** No `.sh`, `.ps1`, `.bat`, or `.py`.
- **Never commit upstream content.** Run `dotnet scripts/verify-no-vendored-content.cs` before any
  commit that adds files in bulk.
- **Documents state current truth**, except `docs/decisions.md` and `docs/gotchas.md`, which are
  append-only and must not be tidied.
- **Baseline:** `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj`
  passes 24 tests in about 11 s before any task begins.

## File Structure

| File | Responsibility |
|---|---|
| `src/DotNetKnowledge.Mcp/Sources/GitCommandRunner.cs` | Starting one git process, redirecting its streams, enforcing its timeout. Modified by Tasks 1 and 2. |
| `src/DotNetKnowledge.Mcp/Sources/GitCommandKind.cs` | **New.** The timeout tier of a git command. Task 2. |
| `src/DotNetKnowledge.Mcp/Sources/GitTimeouts.cs` | **New.** The tier durations, with a seam for tests. Task 2. |
| `src/DotNetKnowledge.Mcp/Sources/SourceSynchronizer.cs` | Sync and state validation. Gains tier arguments (Task 2) and progress reporting (Task 6). |
| `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsModels.cs` | Response records. Gains `ApiLookupOutcome`, resolved type names, and pagination fields. Tasks 3 and 5. |
| `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsQueryService.cs` | Symbol resolution, member matching, paging. Tasks 3, 4, 5. |
| `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsTool.cs` | Error-code mapping and serialization. Tasks 2, 3, 5. |
| `src/DotNetKnowledge.Mcp/Features/Sources/SourcesTool.cs` | `sync_source` progress reporting. Task 6. |
| `tests/DotNetKnowledge.Mcp.Tests.GitRunnerHost/` | **New.** Console fixture that calls the runner from a process whose streams are pipes. Task 1. |
| `tests/DotNetKnowledge.Mcp.Tests/Sources/GitCommandRunnerTests.cs` | **New.** Piped-stdio regression and timeout tests. Tasks 1 and 2. |
| `tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiDocsQueryServiceTests.cs` | Existing query tests; gains fixtures and cases. Tasks 3, 4, 5. |
| `.github/workflows/corpus-tests.yml` | Runs only the corpus suite today. Task 1 adds the MCP suite. |

---

### Task 1: Redirect git's standard input

The blocker. Git blocks during process startup when it inherits a piped stdin handle from a parent
whose own streams are pipes — which is exactly what an MCP client creates. `dotnet test` is a
console host, so no existing test can catch this; the regression test needs its own process.

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Sources/GitCommandRunner.cs:12-23`
- Modify: `src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj`
- Create: `tests/DotNetKnowledge.Mcp.Tests.GitRunnerHost/DotNetKnowledge.Mcp.Tests.GitRunnerHost.csproj`
- Create: `tests/DotNetKnowledge.Mcp.Tests.GitRunnerHost/Program.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj`
- Create: `tests/DotNetKnowledge.Mcp.Tests/Sources/GitCommandRunnerTests.cs`
- Modify: `.github/workflows/corpus-tests.yml`
- Modify: `docs/gotchas.md` (append-only — add an entry, edit nothing)

**Interfaces:**
- Consumes: `GitCommandRunner.RunAsync(string?, IReadOnlyList<string>, CancellationToken)` — unchanged
  in this task.
- Produces: a fixture executable locatable from the test assembly through the assembly metadata key
  `GitRunnerHostPath`, accepting `runner` or `inherit` as its single argument.

- [ ] **Step 1: Create the fixture project**

Create `tests/DotNetKnowledge.Mcp.Tests.GitRunnerHost/DotNetKnowledge.Mcp.Tests.GitRunnerHost.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>DotNetKnowledge.Mcp.Tests.GitRunnerHost</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\DotNetKnowledge.Mcp\DotNetKnowledge.Mcp.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write the fixture program**

Create `tests/DotNetKnowledge.Mcp.Tests.GitRunnerHost/Program.cs`:

```csharp
using System.Diagnostics;
using DotNetKnowledge.Mcp.Sources;

// Calls git from a process that reproduces the fault condition: standard input is a pipe, and this
// process has an outstanding read on it. Both halves are required. A piped stdin alone does not
// hang git — measured at 34 ms — but a piped stdin the parent is concurrently reading hangs it
// indefinitely. An MCP stdio server always has a read pending on stdin, because that read is the
// transport, which is why the fault appears there and nowhere else.
//
// `runner` goes through the real GitCommandRunner. `inherit` deliberately bypasses it with a raw
// ProcessStartInfo, and exists to prove this harness still reproduces the fault the runner prevents.

var mode = args.Length > 0 ? args[0] : "runner";

// The transport-shaped read. Without it neither mode reproduces anything and `runner` passes for
// the wrong reason. Nothing ever writes to this pipe, so the read never completes — which is the
// point.
_ = Task.Run(() => Console.In.ReadToEndAsync());
await Task.Delay(300);

switch (mode)
{
    case "runner":
    {
        var output = await GitCommandRunner.RunAsync(
            Environment.CurrentDirectory,
            ["--version"],
            CancellationToken.None).ConfigureAwait(false);
        Console.Error.WriteLine(output.Trim());
        return 0;
    }

    case "inherit":
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.Start();
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    default:
        Console.Error.WriteLine($"unknown mode '{mode}'");
        return 2;
}
```

- [ ] **Step 3: Grant the fixture and the test project access to the internal runner**

`GitCommandRunner` is `internal`. Add to `src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj`,
immediately after the existing `PackageReference` `ItemGroup`:

```xml
  <ItemGroup>
    <!--
      GitCommandRunner is internal, and the fault it guards against only appears when the calling
      process's standard streams are pipes. Both consumers exist to reproduce that: the fixture
      calls the runner from a redirected child process, and the test project drives the fixture.
    -->
    <InternalsVisibleTo Include="DotNetKnowledge.Mcp.Tests" />
    <InternalsVisibleTo Include="DotNetKnowledge.Mcp.Tests.GitRunnerHost" />
  </ItemGroup>
```

- [ ] **Step 4: Point the test project at the fixture**

Add to `tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj`, inside the existing
`ItemGroup` that holds the `ProjectReference`:

```xml
    <!--
      ReferenceOutputAssembly="false" because the test drives the fixture as a process rather than
      calling into it. The reference exists for build ordering. The path points at the fixture's own
      output directory, where its .runtimeconfig.json sits beside the .dll, so `dotnet <dll>` works.
    -->
    <ProjectReference
      Include="..\DotNetKnowledge.Mcp.Tests.GitRunnerHost\DotNetKnowledge.Mcp.Tests.GitRunnerHost.csproj"
      ReferenceOutputAssembly="false" />
    <AssemblyAttribute Include="System.Reflection.AssemblyMetadataAttribute">
      <_Parameter1>GitRunnerHostPath</_Parameter1>
      <_Parameter2>$(MSBuildThisFileDirectory)..\DotNetKnowledge.Mcp.Tests.GitRunnerHost\bin\$(Configuration)\net10.0\DotNetKnowledge.Mcp.Tests.GitRunnerHost.dll</_Parameter2>
    </AssemblyAttribute>
```

- [ ] **Step 5: Write the failing regression test**

Create `tests/DotNetKnowledge.Mcp.Tests/Sources/GitCommandRunnerTests.cs`:

```csharp
using System.Diagnostics;
using System.Reflection;

namespace DotNetKnowledge.Mcp.Tests.Sources;

[TestClass]
public sealed class GitCommandRunnerTests
{
    [TestMethod]
    public async Task RunnerCompletesWhenTheParentsStreamsArePipes()
    {
        var result = await RunFixtureAsync("runner", TimeSpan.FromSeconds(30));

        Assert.IsFalse(result.TimedOut, "The runner did not complete from a piped-stdio parent.");
        Assert.AreEqual(0, result.ExitCode);
        StringAssert.Contains(result.Stderr, "git version");
    }

    [TestMethod]
    public async Task InheritedStandardInputReproducesTheHang()
    {
        // The control. It bypasses the runner on purpose: if this ever completes, the harness has
        // stopped reproducing the fault and the test above no longer proves anything.
        var result = await RunFixtureAsync("inherit", TimeSpan.FromSeconds(10));

        Assert.IsTrue(
            result.TimedOut,
            "git completed with an inherited stdin handle. The harness no longer reproduces the "
                + "fault, so RunnerCompletesWhenTheParentsStreamsArePipes proves nothing.");
    }

    private static async Task<FixtureResult> RunFixtureAsync(string mode, TimeSpan timeout)
    {
        var fixturePath = typeof(GitCommandRunnerTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "GitRunnerHostPath")
            .Value!;
        Assert.IsTrue(File.Exists(fixturePath), $"Fixture not built: {fixturePath}");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = Path.GetDirectoryName(fixturePath)!,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add(fixturePath);
        process.StartInfo.ArgumentList.Add(mode);

        process.Start();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var timedOut = false;
        using (var cancellation = new CancellationTokenSource(timeout))
        {
            try
            {
                await process.WaitForExitAsync(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
            }
        }

        var stderr = timedOut ? string.Empty : await stderrTask;
        if (timedOut && !process.HasExited)
            process.Kill(entireProcessTree: true);

        return new FixtureResult(timedOut, timedOut ? null : process.ExitCode, stderr);
    }

    private sealed record FixtureResult(bool TimedOut, int? ExitCode, string Stderr);
}
```

- [ ] **Step 6: Run the tests to verify the regression test fails**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter "FullyQualifiedName~GitCommandRunnerTests" --nologo`

Expected: `RunnerCompletesWhenTheParentsStreamsArePipes` FAILS with "The runner did not complete
from a piped-stdio parent." `InheritedStandardInputReproducesTheHang` PASSES.

If the first test passes here, stop — the harness is not reproducing the fault and something in the
fixture wiring is wrong.

- [ ] **Step 7: Apply the fix**

In `src/DotNetKnowledge.Mcp/Sources/GitCommandRunner.cs`, add the redirect to the `ProcessStartInfo`
initializer:

```csharp
                RedirectStandardOutput = true,
                RedirectStandardError = true,

                // Git blocks during process startup when it inherits a piped stdin handle from a
                // parent whose own streams are pipes — which is what an MCP client creates. Even
                // `git --version` hangs. Redirecting is what fixes it; the stream is closed below
                // so no future invocation can block on a handle that never reaches end of file.
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
```

Then immediately after the `try`/`catch` around `process.Start()`, before the stdout and stderr
reads:

```csharp
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --nologo`

Expected: PASS, 26 tests. The control test still takes about 10 s, which is the cost of a
regression test known to be capable of failing.

- [ ] **Step 9: Correct the gotchas entry for the real trigger**

`docs/gotchas.md` records the condition as an inherited piped stdin handle. That is necessary but
not sufficient: a piped stdin alone completes in about 34 ms, and only a piped stdin the parent is
concurrently reading hangs. The file is **append-only** — add this entry at the top of the entry
list, directly under the `---`, and edit nothing below it:

```markdown
### 2026-08-05 · git hangs only when the parent is *reading* the inherited stdin · environment

A piped stdin alone does not hang git — measured at 34 ms. It hangs when the parent has an
outstanding read on the same handle, which an MCP stdio server always does because that read is the
transport. Supersedes the earlier 2026-08-05 entry, which named the pipe alone as the cause.
`RedirectStandardInput = true` remains the fix. Reproduce: `tests/DotNetKnowledge.Mcp.Tests.GitRunnerHost`.
```

- [ ] **Step 10: Add the MCP suite to CI**

The regression test protects nothing if CI never runs it — today `.github/workflows/corpus-tests.yml`
runs only the corpus suite. Add a step after the existing corpus test step, matching its style:

```yaml
      - name: Test the MCP server
        shell: pwsh
        run: dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --configuration Release --nologo --logger "trx;LogFileName=mcp-tests.trx"
```

Use plain `dotnet`, not `$env:DOTNET_HOST_PATH`. That variable is re-derived inside each step that
needs it, because a `$env:` assignment does not survive from one workflow step's process into the
next — a step referencing it without setting it invokes an empty command and fails every run. The
MCP tests do not need the private corpus SDK host regardless: they target `net10.0` and run on the
SDK `actions/setup-dotnet` installed, exactly like the `Verify no vendored content` step below.

- [ ] **Step 11: Commit**

```bash
git add src/DotNetKnowledge.Mcp/Sources/GitCommandRunner.cs \
        src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj \
        tests/DotNetKnowledge.Mcp.Tests.GitRunnerHost/ \
        tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj \
        tests/DotNetKnowledge.Mcp.Tests/Sources/GitCommandRunnerTests.cs \
        .github/workflows/corpus-tests.yml \n        docs/gotchas.md
git commit -m "fix: redirect git's standard input so it cannot inherit a piped handle"
```

---

### Task 2: Give git commands timeout tiers

An unbounded hang is unrecoverable. A failure that names the command that exceeded its tier is not.

**Files:**
- Create: `src/DotNetKnowledge.Mcp/Sources/GitCommandKind.cs`
- Create: `src/DotNetKnowledge.Mcp/Sources/GitTimeouts.cs`
- Modify: `src/DotNetKnowledge.Mcp/Sources/GitCommandRunner.cs`
- Modify: `src/DotNetKnowledge.Mcp/Sources/SourceSynchronizer.cs:86-97,130-133,196-220`
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsTool.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Sources/GitCommandRunnerTests.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests.GitRunnerHost/Program.cs`

**Interfaces:**
- Consumes: `GitCommandRunner.RunAsync` from Task 1.
- Produces:
  - `internal enum GitCommandKind { Quick, Bulk }`
  - `internal sealed record GitTimeouts(TimeSpan Quick, TimeSpan Bulk)` with
    `static GitTimeouts Default { get; }`
  - `GitCommandRunner.RunAsync(string? workingDirectory, IReadOnlyList<string> arguments, GitCommandKind kind, CancellationToken cancellationToken, GitTimeouts? timeouts = null)`
  - On expiry: `TimeoutException` whose message contains the full git command.

> **Deviation from the spec, flagged for review.** The spec names the tiers `Local` and `Network`
> and classifies only `rev-parse`, `config`, `status` (local) and `clone`, `fetch` (network). It does
> not classify `sparse-checkout set` or `checkout --detach FETCH_HEAD`, both of which write the
> entire working tree — 806 MB for `dotnet-api-docs` — and so belong nowhere near a ten-second
> ceiling despite using no network. The tiers are therefore named for expected duration rather than
> for the network, and those two commands are `Bulk`. Tier durations are unchanged from the spec:
> ten seconds and fifteen minutes.

- [ ] **Step 1: Write the failing timeout test**

Append to `tests/DotNetKnowledge.Mcp.Tests/Sources/GitCommandRunnerTests.cs`, inside the class:

```csharp
    [TestMethod]
    public async Task TimeoutNamesTheCommandThatExceededItsTier()
    {
        // A one-millisecond ceiling on a command that cannot finish before process start completes.
        // Deterministic, and it exercises the real timeout path rather than a simulated one.
        var timeouts = new GitTimeouts(TimeSpan.FromMilliseconds(1), TimeSpan.FromMinutes(15));

        var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            GitCommandRunner.RunAsync(
                Environment.CurrentDirectory,
                ["--version"],
                GitCommandKind.Quick,
                CancellationToken.None,
                timeouts));

        StringAssert.Contains(exception.Message, "git --version");
    }

    [TestMethod]
    public async Task CallerCancellationIsNotReportedAsATimeout()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // The caller getting what it asked for is not a fault. Asserting the negative rather than
        // an exact exception type keeps this robust: the cancellation can surface from the wait or
        // from either stream read, which differ in the derived type they throw.
        try
        {
            await GitCommandRunner.RunAsync(
                Environment.CurrentDirectory,
                ["--version"],
                GitCommandKind.Quick,
                cancellation.Token);
            Assert.Fail("Expected the cancelled token to abort the command.");
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }
        catch (TimeoutException)
        {
            Assert.Fail("Caller cancellation was misreported as a tier timeout.");
        }
    }
```

Add `using DotNetKnowledge.Mcp.Sources;` to the file's usings.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter "FullyQualifiedName~GitCommandRunnerTests" --nologo`

Expected: FAIL to compile — `GitCommandKind` and `GitTimeouts` do not exist.

- [ ] **Step 3: Add the tier types**

Create `src/DotNetKnowledge.Mcp/Sources/GitCommandKind.cs`:

```csharp
namespace DotNetKnowledge.Mcp.Sources;

/// <summary>How long a git command is expected to take, which selects its timeout tier.</summary>
internal enum GitCommandKind
{
    /// <summary>Reads metadata and touches few files: rev-parse, config, status.</summary>
    Quick,

    /// <summary>Transfers or writes the whole working tree: clone, fetch, sparse-checkout, checkout.</summary>
    Bulk,
}
```

Create `src/DotNetKnowledge.Mcp/Sources/GitTimeouts.cs`:

```csharp
namespace DotNetKnowledge.Mcp.Sources;

/// <summary>
/// The ceiling for each <see cref="GitCommandKind"/>. A timeout that fires on a healthy repository
/// is a worse defect than an unbounded hang, so both values carry deliberate margin.
/// </summary>
internal sealed record GitTimeouts(TimeSpan Quick, TimeSpan Bulk)
{
    /// <summary>
    /// Ten seconds for metadata commands. Fifteen minutes for bulk ones — roughly five times the
    /// measured worst case, a 2 min 57 s clone of dotnet-api-docs at 806 MB.
    /// </summary>
    public static GitTimeouts Default { get; } =
        new(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(15));

    public TimeSpan For(GitCommandKind kind) => kind == GitCommandKind.Quick ? Quick : Bulk;
}
```

- [ ] **Step 4: Enforce the timeout in the runner**

Replace the body of `GitCommandRunner.RunAsync` from its signature through the end of the method:

```csharp
    public static async Task<string> RunAsync(
        string? workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandKind kind,
        CancellationToken cancellationToken,
        GitTimeouts? timeouts = null)
    {
        var ceiling = (timeouts ?? GitTimeouts.Default).For(kind);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,

                // Git blocks during process startup when it inherits a piped stdin handle from a
                // parent whose own streams are pipes — which is what an MCP client creates. Even
                // `git --version` hangs. Redirecting is what fixes it; the stream is closed below
                // so no future invocation can block on a handle that never reaches end of file.
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        process.StartInfo.Environment["GCM_INTERACTIVE"] = "Never";

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException("Could not start git. Install Git and ensure it is on PATH.", exception);
        }

        process.StandardInput.Close();

        using var expiry = new CancellationTokenSource(ceiling);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, expiry.Token);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);

        // The caller's cancellation and an expired tier both surface as OperationCanceledException.
        // Only the second is a fault worth naming; the first is the caller getting what it asked
        // for. Both outcomes must kill the tree, so the decision is factored rather than repeated.
        void KillIfRunning()
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }

        bool TierExpired() =>
            expiry.IsCancellationRequested && !cancellationToken.IsCancellationRequested;

        TimeoutException Expired() =>
            new($"git {string.Join(' ', arguments)} exceeded its {ceiling.TotalSeconds:0.##}s "
                + $"{kind} timeout and was terminated.");

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillIfRunning();
            if (TierExpired())
                throw Expired();
            throw;
        }

        string stdout;
        string stderr;
        try
        {
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // git itself exited inside its ceiling, but a descendant — a credential helper, a
            // background `gc --auto` — inherited the redirected pipes and kept them open, so the
            // reads outlived the process. Unguarded, this escapes as a raw cancellation that no
            // tool translates, and it skips the kill, leaking the descendant.
            KillIfRunning();
            if (TierExpired())
                throw Expired();
            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} exited with {process.ExitCode}: {stderr.Trim()}");
        }

        return stdout;
    }
```

- [ ] **Step 5: Classify every call site**

In `src/DotNetKnowledge.Mcp/Sources/SourceSynchronizer.cs`, add the kind argument after the
arguments array at each call. `TryGetCurrentStateCoreAsync` — all three are `Quick`:

```csharp
            var actualCommit = (await GitCommandRunner.RunAsync(
                directory,
                ["rev-parse", "HEAD"],
                GitCommandKind.Quick,
                cancellationToken).ConfigureAwait(false)).Trim();
            var origin = (await GitCommandRunner.RunAsync(
                directory,
                ["config", "--get", "remote.origin.url"],
                GitCommandKind.Quick,
                cancellationToken).ConfigureAwait(false)).Trim();
            var status = await GitCommandRunner.RunAsync(
                directory,
                ["status", "--porcelain", "--untracked-files=all"],
                GitCommandKind.Quick,
                cancellationToken).ConfigureAwait(false);
```

`SyncCoreAsync` — the clone is `Bulk`:

```csharp
            await GitCommandRunner.RunAsync(
                null,
                ["clone", "--filter=blob:none", "--no-checkout", "--sparse", "--quiet", "--", definition.Url, staging],
                GitCommandKind.Bulk,
                cancellationToken).ConfigureAwait(false);
```

`SynchronizeRepositoryAsync` — `sparse-checkout`, `fetch` and `checkout` write or transfer the tree
and are `Bulk`; the three validation commands after them are `Quick`:

```csharp
        await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["sparse-checkout", "set", .. definition.Sparse],
            GitCommandKind.Bulk,
            cancellationToken).ConfigureAwait(false);
        await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["fetch", "--depth", "1", "--quiet", "origin", target],
            GitCommandKind.Bulk,
            cancellationToken).ConfigureAwait(false);
        await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["checkout", "--detach", "--quiet", "FETCH_HEAD"],
            GitCommandKind.Bulk,
            cancellationToken).ConfigureAwait(false);

        var commit = (await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["rev-parse", "HEAD"],
            GitCommandKind.Quick,
            cancellationToken).ConfigureAwait(false)).Trim();
        var origin = (await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["config", "--get", "remote.origin.url"],
            GitCommandKind.Quick,
            cancellationToken).ConfigureAwait(false)).Trim();
        var status = await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["status", "--porcelain", "--untracked-files=all"],
            GitCommandKind.Quick,
            cancellationToken).ConfigureAwait(false);
```

- [ ] **Step 6: Update the fixture to the new signature**

In `tests/DotNetKnowledge.Mcp.Tests.GitRunnerHost/Program.cs`, the `runner` case becomes:

```csharp
        var output = await GitCommandRunner.RunAsync(
            Environment.CurrentDirectory,
            ["--version"],
            GitCommandKind.Quick,
            CancellationToken.None).ConfigureAwait(false);
```

- [ ] **Step 7: Surface a timeout as a tool error rather than an unhandled exception**

`TimeoutException` does not derive from `InvalidOperationException`, so it deliberately escapes
`TryGetCurrentStateCoreAsync`'s catch instead of masquerading as "not synced". It must still reach
the caller as a structured error. In `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsTool.cs`, add
this catch to **both** `LookupApi` and `SearchApi`, immediately before the existing
`catch (ArgumentException ...)`:

```csharp
        catch (TimeoutException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "git_timeout",
                    message = exception.Message,
                },
                WriteOptions);
        }
```

`ListSources` needs it too, and it is the most exposed of the four: it fans out over *every*
configured source, and each one runs three `Quick`-tier commands through `TryGetCurrentStateAsync`.
It currently has no `try`/`catch` at all, so a timeout escapes unhandled from the one tool that
works today. Name the source, which `Task.WhenAll` would otherwise lose — in
`src/DotNetKnowledge.Mcp/Features/Sources/SourcesTool.cs`, wrap the body of `GetSourceStatusAsync`:

```csharp
        SourceSyncState? state;
        try
        {
            state = await synchronizer.TryGetCurrentStateAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            // Task.WhenAll would otherwise surface a message naming the git command but not which
            // source it was validating, and this tool touches all of them.
            throw new TimeoutException($"{name}: {exception.Message}", exception);
        }
```

and wrap `ListSources`'s body from `var sourceDefinitions = ...` through its `return` in:

```csharp
        try
        {
            // ... existing body ...
        }
        catch (TimeoutException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "git_timeout",
                    message = exception.Message,
                },
                WriteOptions);
        }
```

A partial listing is not offered here on purpose: a source that silently reported "not synced"
because its validation timed out is exactly the plausible-absence failure this server is built to
avoid.

And add the same catch to `SyncSource` immediately before its
`catch (InvalidOperationException ...)`:

```csharp
        catch (TimeoutException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "git_timeout",
                    message = exception.Message,
                    source = name,
                },
                WriteOptions);
        }
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --nologo`

Expected: PASS, 28 tests.

- [ ] **Step 9: Measure the Quick tier against a real repository**

The ten-second ceiling is a guess until measured against the worst case: `git status` walking a
fully synchronized `dotnet-api-docs` working tree. From a console, with the source already synced:

```bash
dotnet run --project src/DotNetKnowledge.Mcp   # then call sync_source(name: "dotnet-api-docs")
# once synced, against %LOCALAPPDATA%\dotnet-knowledge\sources\dotnet-api-docs:
git status --porcelain --untracked-files=all
```

Record the elapsed time. If it exceeds about three seconds, raise `GitTimeouts.Default.Quick` to at
least five times the measurement and say so in the record. Add the result to `docs/gotchas.md` as a
new entry — that file is append-only, so add, never edit.

- [ ] **Step 10: Commit**

```bash
git add src/DotNetKnowledge.Mcp/Sources/ \
        src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsTool.cs \
        src/DotNetKnowledge.Mcp/Features/Sources/SourcesTool.cs \
        tests/DotNetKnowledge.Mcp.Tests/Sources/GitCommandRunnerTests.cs \
        tests/DotNetKnowledge.Mcp.Tests.GitRunnerHost/Program.cs \
        docs/gotchas.md
git commit -m "feat: bound every git command with a timeout tier that names what expired"
```

---

### Task 3: Make generic members reachable by name

`lookup_api("Enumerable.Select")` returns `not_found` today, because the ECMA `MemberName` attribute
carries the type-parameter list — `Select&lt;TSource,TResult&gt;` — and the match is ordinal
equality. Every LINQ operator is affected.

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsModels.cs:26-28`
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsQueryService.cs:30-68,145-159,242-259`
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsTool.cs:32-44`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiDocsQueryServiceTests.cs`

**Interfaces:**
- Produces:
  - `public enum ApiLookupOutcome { Found, TypeNotFound, MemberNotFound }`
  - `ApiLookupResult(IReadOnlyList<ApiTypeDocumentation> Matches, IReadOnlyList<SourceProvenance> SearchedSources, ApiLookupOutcome Outcome, IReadOnlyList<string> ResolvedTypeNames)`
  - `lookup_api` error code `member_not_found`, carrying `resolvedTypes`.

- [ ] **Step 1: Write the failing tests**

In `tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiDocsQueryServiceTests.cs`, extend the
`Widget.xml` fixture written in `LookupAsyncReturnsDocumentedMemberWithProvenance` by adding two
generic members inside `<Members>`, immediately after the existing `Create` member:

```xml
                    <Member MemberName="Convert&lt;TResult&gt;">
                      <MemberSignature Language="C#" Value="public TResult Convert&lt;TResult&gt;();" />
                      <Docs><summary>Converts to one type.</summary></Docs>
                    </Member>
                    <Member MemberName="Convert&lt;TResult,TState&gt;">
                      <MemberSignature Language="C#" Value="public TResult Convert&lt;TResult,TState&gt;(TState state);" />
                      <Docs><summary>Converts with state.</summary></Docs>
                    </Member>
```

Then add a new test method to the class:

```csharp
    [TestMethod]
    public async Task LookupAsyncMatchesGenericMembersByPlainNameAndSeparatesMissingKinds()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);

            // Every arity of a requested name comes back. A caller asking for "Convert" cannot
            // otherwise discover which arities exist.
            var byPlainName = await service.LookupAsync("Widget.Convert", "dotnet-api-docs", CancellationToken.None);
            Assert.AreEqual(ApiLookupOutcome.Found, byPlainName.Outcome);
            Assert.HasCount(2, byPlainName.Matches[0].Members);

            // The fully-specified form still matches, and selects one arity.
            var bySpecificArity = await service.LookupAsync(
                "Widget.Convert<TResult>", "dotnet-api-docs", CancellationToken.None);
            Assert.AreEqual(ApiLookupOutcome.Found, bySpecificArity.Outcome);
            Assert.HasCount(1, bySpecificArity.Matches[0].Members);

            // A type that does not exist and a member that does not exist are different failures.
            var noSuchType = await service.LookupAsync(
                "System.MissingWidget", "dotnet-api-docs", CancellationToken.None);
            Assert.AreEqual(ApiLookupOutcome.TypeNotFound, noSuchType.Outcome);
            Assert.IsEmpty(noSuchType.ResolvedTypeNames);

            var noSuchMember = await service.LookupAsync(
                "Widget.NotAMember", "dotnet-api-docs", CancellationToken.None);
            Assert.AreEqual(ApiLookupOutcome.MemberNotFound, noSuchMember.Outcome);
            CollectionAssert.AreEqual(new[] { "System.Widget" }, noSuchMember.ResolvedTypeNames.ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }
```

Extract the repeated setup from `LookupAsyncReturnsDocumentedMemberWithProvenance` into a helper on
the class, and call it from both tests:

```csharp
    private static async Task<ApiDocsQueryService> CreateWidgetServiceAsync(string root)
    {
        var repository = Path.Combine(root, "origin");
        var namespaceDirectory = Path.Combine(repository, "xml", "System");
        Directory.CreateDirectory(namespaceDirectory);
        await RunGitAsync(null, "init", "--initial-branch=main", repository);
        await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(repository, "config", "user.name", "Tests");
        await File.WriteAllTextAsync(Path.Combine(namespaceDirectory, "Widget.xml"), WidgetXml);
        await RunGitAsync(repository, "add", ".");
        await RunGitAsync(repository, "commit", "-m", "docs");
        var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
        var catalogPath = Path.Combine(root, "sources.json");
        await WriteCatalogAsync(catalogPath, repository, pin);
        var catalog = new SourceCatalog(catalogPath);
        var cache = new SourceCache(Path.Combine(root, "cache"));
        var synchronizer = new SourceSynchronizer(catalog, cache);
        await synchronizer.SyncAsync("dotnet-api-docs", requestedRef: null, CancellationToken.None);
        return new ApiDocsQueryService(catalog, cache, synchronizer);
    }
```

Hold the fixture XML in a `private const string WidgetXml` on the class so both tests share it.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter "FullyQualifiedName~ApiDocsQueryServiceTests" --nologo`

Expected: FAIL to compile — `ApiLookupOutcome`, `Outcome` and `ResolvedTypeNames` do not exist.

- [ ] **Step 3: Extend the result model**

In `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsModels.cs`, replace the `ApiLookupResult`
record and add the enum above it:

```csharp
/// <summary>
/// Why a lookup returned nothing. A type that does not exist and a member that does not exist need
/// different remedies, and only the second is answerable with another lookup.
/// </summary>
public enum ApiLookupOutcome
{
    Found,
    TypeNotFound,
    MemberNotFound,
}

public sealed record ApiLookupResult(
    IReadOnlyList<ApiTypeDocumentation> Matches,
    IReadOnlyList<SourceProvenance> SearchedSources,
    ApiLookupOutcome Outcome,
    IReadOnlyList<string> ResolvedTypeNames);
```

- [ ] **Step 4: Match member names by their generic-free prefix**

In `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsQueryService.cs`, add the matcher as a private
static method:

```csharp
    /// <summary>
    /// ECMA XML spells a generic member's MemberName with its type-parameter list, so
    /// "Select&lt;TSource,TResult&gt;" is what an agent asking for "Select" must match. The fully
    /// specified form is still accepted, which is how a caller selects one arity.
    /// </summary>
    private static bool MemberNameMatches(string? attributeValue, string requested)
    {
        if (attributeValue is null)
            return false;
        if (string.Equals(attributeValue, requested, StringComparison.Ordinal))
            return true;

        var typeParameters = attributeValue.IndexOf('<', StringComparison.Ordinal);
        return typeParameters > 0
            && string.Equals(attributeValue[..typeParameters], requested, StringComparison.Ordinal);
    }
```

Replace the member filter inside `ReadType`:

```csharp
        var members = root.Descendants("Member")
            .Where(member => memberName is null
                || MemberNameMatches(member.Attribute("MemberName")?.Value, memberName))
            .Select(ReadMember)
```

- [ ] **Step 5: Carry the resolved type names out of the read**

Replace `ReadLookupSource` and add the record it returns:

```csharp
    private static LookupRead ReadLookupSource(
        string sourceName,
        string directory,
        string symbol,
        SourceDefinition definition,
        SourceSyncState state)
    {
        var docsRoot = ResolveDocsRoot(sourceName, directory);
        var (files, memberName) = ResolveSymbol(docsRoot, symbol);
        var documented = files
            .Select(file => ReadType(file, memberName, definition, state))
            .ToArray();

        return new LookupRead(
            ToProvenance(definition, state),
            documented.Where(type => memberName is null || type.Members.Count > 0).ToArray(),

            // Every type whose name matched, before member filtering. This is what distinguishes
            // "no such type" from "the type exists and the member did not match".
            documented.Select(type => type.FullName).ToArray());
    }

    private sealed record LookupRead(
        SourceProvenance Provenance,
        IReadOnlyList<ApiTypeDocumentation> Matches,
        IReadOnlyList<string> ResolvedTypeNames);
```

- [ ] **Step 6: Decide the outcome in `LookupAsync`**

Replace the body of `LookupAsync` after `ValidateSymbol(symbol);`:

```csharp
        var sourceNames = ResolveSourceNames(source);
        var matches = new List<ApiTypeDocumentation>();
        var resolvedTypeNames = new List<string>();
        var searchedSources = new List<SourceProvenance>();

        foreach (var sourceName in sourceNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LookupRead read;
            try
            {
                read = await _synchronizer.ReadCurrentSourceAsync(
                    sourceName,
                    (definition, state, directory) => ReadLookupSource(
                        sourceName, directory, symbol, definition, state),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw new SourceNotSyncedException(sourceName, exception);
            }

            searchedSources.Add(read.Provenance);
            matches.AddRange(read.Matches);
            resolvedTypeNames.AddRange(read.ResolvedTypeNames);
        }

        var ordered = matches
            .OrderBy(match => match.FullName, StringComparer.Ordinal)
            .ThenBy(match => match.Source.Repo, StringComparer.Ordinal)
            .ToArray();
        var outcome = ordered.Length > 0
            ? ApiLookupOutcome.Found
            : resolvedTypeNames.Count > 0
                ? ApiLookupOutcome.MemberNotFound
                : ApiLookupOutcome.TypeNotFound;

        return new ApiLookupResult(
            ordered,
            searchedSources,
            outcome,
            resolvedTypeNames.Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
```

- [ ] **Step 7: Give each outcome its own error and remedy**

In `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsTool.cs`, replace the `result.Matches.Count == 0`
block in `LookupApi`:

```csharp
            if (result.Matches.Count == 0)
            {
                // Directing a caller to search_api is right when the type was not found and wrong
                // when the type resolved: search_api enumerates file names and never opens a
                // document, so no search of it can surface a member.
                var memberMissing = result.Outcome == ApiLookupOutcome.MemberNotFound;
                return JsonSerializer.Serialize(
                    new
                    {
                        error = memberMissing ? "member_not_found" : "not_found",
                        message = memberMissing
                            ? $"No member of '{string.Join("', '", result.ResolvedTypeNames)}' matches "
                                + $"'{symbol}'. Call lookup_api with just the type name to list its members."
                            : $"API symbol '{symbol}' was not found in the selected synchronized source(s). "
                                + "Call search_api with a type-name fragment to find candidates.",
                        symbol,
                        resolvedTypes = memberMissing ? result.ResolvedTypeNames : null,
                        searchedSources = result.SearchedSources,
                    },
                    WriteOptions);
            }
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --nologo`

Expected: PASS, 29 tests.

- [ ] **Step 9: Commit**

```bash
git add src/DotNetKnowledge.Mcp/Features/ApiDocs/ \
        tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiDocsQueryServiceTests.cs
git commit -m "fix: match generic members by name and separate a missing type from a missing member"
```

---

### Task 4: Stop a non-generic type shadowing its generic namesake

`FindTypeFilesInNamespace` returns the exact `<simpleName>.xml` alone whenever it exists, so
`SyntaxList` resolves to a static helper with one method and never to `SyntaxList<TNode>`. The
response is not visibly incomplete, which is what makes it dangerous.

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsQueryService.cs:226-240`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiDocsQueryServiceTests.cs`

**Interfaces:**
- Consumes: `ApiLookupResult.Outcome` from Task 3.
- Produces: no signature change. `FindTypeFilesInNamespace` returns the exact match followed by every
  generic arity.

- [ ] **Step 1: Write the failing test**

Add to `ApiDocsQueryServiceTests`:

```csharp
    [TestMethod]
    public async Task LookupAsyncReturnsBothTheNonGenericTypeAndItsGenericNamesake()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var repository = Path.Combine(root, "origin");
            var namespaceDirectory = Path.Combine(repository, "xml", "System");
            Directory.CreateDirectory(namespaceDirectory);
            await RunGitAsync(null, "init", "--initial-branch=main", repository);
            await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
            await RunGitAsync(repository, "config", "user.name", "Tests");
            await File.WriteAllTextAsync(
                Path.Combine(namespaceDirectory, "Holder.xml"),
                "<Type Name=\"Holder\" FullName=\"System.Holder\" />");
            await File.WriteAllTextAsync(
                Path.Combine(namespaceDirectory, "Holder`1.xml"),
                "<Type Name=\"Holder`1\" FullName=\"System.Holder&lt;T&gt;\" />");
            await RunGitAsync(repository, "add", ".");
            await RunGitAsync(repository, "commit", "-m", "docs");
            var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
            var catalogPath = Path.Combine(root, "sources.json");
            await WriteCatalogAsync(catalogPath, repository, pin);
            var catalog = new SourceCatalog(catalogPath);
            var cache = new SourceCache(Path.Combine(root, "cache"));
            var synchronizer = new SourceSynchronizer(catalog, cache);
            await synchronizer.SyncAsync("dotnet-api-docs", requestedRef: null, CancellationToken.None);
            var service = new ApiDocsQueryService(catalog, cache, synchronizer);

            var result = await service.LookupAsync("Holder", "dotnet-api-docs", CancellationToken.None);

            Assert.AreEqual(ApiLookupOutcome.Found, result.Outcome);
            CollectionAssert.AreEqual(
                new[] { "System.Holder", "System.Holder<T>" },
                result.Matches.Select(match => match.FullName).ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter "FullyQualifiedName~ReturnsBothTheNonGeneric" --nologo`

Expected: FAIL — one match, `System.Holder`, because the exact file shadowed the arity glob.

- [ ] **Step 3: Return the exact match and the arity glob**

Replace `FindTypeFilesInNamespace`:

```csharp
    private static string[] FindTypeFilesInNamespace(
        string namespaceDirectory,
        string simpleName)
    {
        if (!Directory.Exists(namespaceDirectory))
            return [];

        // Both, not one or the other. A namespace holding SyntaxList and SyntaxList`1 would
        // otherwise answer for the plain name with the smaller type and say nothing about the
        // larger one, which reads as a complete result.
        var files = new List<string>();
        var exact = Path.Combine(namespaceDirectory, simpleName + ".xml");
        if (File.Exists(exact))
            files.Add(exact);

        files.AddRange(Directory
            .EnumerateFiles(namespaceDirectory, simpleName + "`*.xml")
            .OrderBy(path => path, StringComparer.Ordinal));
        return files.ToArray();
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --nologo`

Expected: PASS, 30 tests.

- [ ] **Step 5: Commit**

```bash
git add src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsQueryService.cs \
        tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiDocsQueryServiceTests.cs
git commit -m "fix: return both the exact type match and its generic arities"
```

---

### Task 5: Give `lookup_api` a response budget

`lookup_api("String")` returns 235 members with full documentation — 427 817 bytes, roughly 107 000
tokens. The shape of the requested symbol now selects the detail level, and results paginate.

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsModels.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsQueryService.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/ApiDocs/ApiDocsTool.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiDocsQueryServiceTests.cs`

**Interfaces:**
- Consumes: `ApiLookupOutcome`, `LookupRead` from Task 3.
- Produces:
  - `ApiMemberDocumentation(string Name, string Signature, string? Summary, IReadOnlyList<ApiParameterDocumentation>? Parameters, string? Returns, string? Remarks)` — `Parameters` becomes nullable so it is omitted in signatures mode.
  - `ApiLookupResult(IReadOnlyList<ApiTypeDocumentation> Matches, IReadOnlyList<SourceProvenance> SearchedSources, ApiLookupOutcome Outcome, IReadOnlyList<string> ResolvedTypeNames, bool IsPartial, string? NextPageToken)`
  - `ApiDocsQueryService.LookupAsync(string symbol, string? source, int limit, string? cursor, CancellationToken cancellationToken)`
  - `lookup_api` gains optional `limit` (1–100, default 20) and `cursor` parameters.

- [ ] **Step 1: Write the failing tests**

Add to `ApiDocsQueryServiceTests`:

```csharp
    [TestMethod]
    public async Task LookupAsyncBudgetsWholeTypeResponsesAndPaginates()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);

            // A bare type name is signatures only. remarks is the largest contributor to response
            // size and appears only when a caller named the member it belongs to.
            var wholeType = await service.LookupAsync(
                "Widget", "dotnet-api-docs", limit: 20, cursor: null, CancellationToken.None);
            Assert.AreEqual(ApiLookupOutcome.Found, wholeType.Outcome);
            Assert.HasCount(3, wholeType.Matches[0].Members);
            foreach (var member in wholeType.Matches[0].Members)
            {
                Assert.IsNotNull(member.Signature);
                Assert.IsNull(member.Summary);
                Assert.IsNull(member.Remarks);
                Assert.IsNull(member.Parameters);
            }

            // Naming a member restores full documentation.
            var oneMember = await service.LookupAsync(
                "Widget.Create", "dotnet-api-docs", limit: 20, cursor: null, CancellationToken.None);
            Assert.AreEqual("Creates a widget.", oneMember.Matches[0].Members[0].Summary);
            Assert.AreEqual("Names are case-sensitive.", oneMember.Matches[0].Members[0].Remarks);
            Assert.AreEqual("The widget name.", oneMember.Matches[0].Members[0].Parameters![0].Description);

            // Paging is over a flat member sequence, so a page boundary can fall inside a type.
            var firstPage = await service.LookupAsync(
                "Widget", "dotnet-api-docs", limit: 2, cursor: null, CancellationToken.None);
            Assert.HasCount(2, firstPage.Matches[0].Members);
            Assert.IsTrue(firstPage.IsPartial);
            Assert.IsNotNull(firstPage.NextPageToken);

            var secondPage = await service.LookupAsync(
                "Widget", "dotnet-api-docs", limit: 2, firstPage.NextPageToken, CancellationToken.None);
            Assert.HasCount(1, secondPage.Matches[0].Members);
            Assert.IsFalse(secondPage.IsPartial);
            Assert.IsNull(secondPage.NextPageToken);

            // A cursor issued for one symbol must not be honored for another.
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.LookupAsync(
                "Widget.Create", "dotnet-api-docs", limit: 2, firstPage.NextPageToken, CancellationToken.None));

            // A search cursor must not be honored by lookup.
            var search = await service.SearchAsync("Widget", limit: 1, cursor: null, CancellationToken.None);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.LookupAsync(
                "Widget", "dotnet-api-docs", limit: 2, search.NextPageToken, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }
```

Update the existing `LookupAsyncReturnsDocumentedMemberWithProvenance` and
`LookupAsyncMatchesGenericMembersByPlainNameAndSeparatesMissingKinds` calls to the new five-argument
signature, passing `limit: 20, cursor: null`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter "FullyQualifiedName~ApiDocsQueryServiceTests" --nologo`

Expected: FAIL to compile — `LookupAsync` has no `limit` or `cursor` parameter.

- [ ] **Step 3: Make `Parameters` optional and add the pagination fields**

In `ApiDocsModels.cs`:

```csharp
public sealed record ApiMemberDocumentation(
    string Name,
    string Signature,
    string? Summary,
    IReadOnlyList<ApiParameterDocumentation>? Parameters,
    string? Returns,
    string? Remarks);
```

```csharp
public sealed record ApiLookupResult(
    IReadOnlyList<ApiTypeDocumentation> Matches,
    IReadOnlyList<SourceProvenance> SearchedSources,
    ApiLookupOutcome Outcome,
    IReadOnlyList<string> ResolvedTypeNames,
    bool IsPartial,
    string? NextPageToken);
```

- [ ] **Step 4: Give the two cursor kinds one codec that cannot confuse them**

In `ApiDocsQueryService.cs`, replace `EncodeCursor`, `DecodeCursor` and the `SearchCursor` record:

```csharp
    private static string EncodeCursor(string kind, string scope, int offset, IReadOnlyList<string> revisions)
    {
        var json = JsonSerializer.Serialize(
            new PageCursor(Version: 1, Kind: kind, Scope: scope, Offset: offset, Revisions: revisions));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static int DecodeCursor(string? cursor, string kind, string scope, IReadOnlyList<string> revisions)
    {
        if (cursor is null)
            return 0;

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var decoded = JsonSerializer.Deserialize<PageCursor>(
                Encoding.UTF8.GetString(Convert.FromBase64String(base64)));

            // Kind keeps a search cursor from being honored by a lookup; scope keeps a cursor for
            // one symbol or pattern from being honored for another; revisions keep any cursor from
            // surviving a re-synchronization that changes what it points at.
            if (decoded is null
                || decoded.Version != 1
                || decoded.Offset < 0
                || !string.Equals(decoded.Kind, kind, StringComparison.Ordinal)
                || !string.Equals(decoded.Scope, scope, StringComparison.Ordinal)
                || decoded.Revisions is null
                || !decoded.Revisions.SequenceEqual(revisions, StringComparer.Ordinal))
            {
                throw new ArgumentException("cursor does not match this request.", nameof(cursor));
            }

            return decoded.Offset;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new ArgumentException("cursor is invalid.", nameof(cursor), exception);
        }
    }

    private sealed record PageCursor(
        int Version,
        string Kind,
        string Scope,
        int Offset,
        IReadOnlyList<string> Revisions);
```

Update the two calls inside `SearchAsync` to pass the kind and scope:

```csharp
        var offset = DecodeCursor(cursor, "search", pattern, revisions);
```

```csharp
            NextPageToken: isPartial ? EncodeCursor("search", pattern, nextOffset, revisions) : null,
```

- [ ] **Step 5: Page and project in `LookupAsync`**

Replace the `LookupAsync` signature and the block that follows the source loop:

```csharp
    public async Task<ApiLookupResult> LookupAsync(
        string symbol,
        string? source,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ValidateSymbol(symbol);
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 100.");
```

Keep the source loop from Task 3 unchanged, then replace everything after it:

```csharp
        var ordered = matches
            .OrderBy(match => match.FullName, StringComparer.Ordinal)
            .ThenBy(match => match.Source.Repo, StringComparer.Ordinal)
            .ToArray();
        var outcome = ordered.Length > 0
            ? ApiLookupOutcome.Found
            : resolvedTypeNames.Count > 0
                ? ApiLookupOutcome.MemberNotFound
                : ApiLookupOutcome.TypeNotFound;
        var distinctTypeNames = resolvedTypeNames.Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // A bare type name asks for an inventory; naming a member asks for its documentation. The
        // symbol is the whole selector, so the expensive response is unreachable rather than
        // merely opt-out. The reads already answered this question when they resolved the symbol,
        // so it is carried out rather than recomputed — recomputing risks the two disagreeing.
        var signaturesOnly = reads.Any(read => read.Matches.Count > 0 && !read.SymbolNamedAMember);

        // Paging runs over one flat, ordinally-ordered member sequence across every match, so a
        // three-type result such as List has one pagination state rather than three.
        var pairs = ordered
            .SelectMany(type => type.Members
                .OrderBy(member => member.Name, StringComparer.Ordinal)
                .ThenBy(member => member.Signature, StringComparer.Ordinal)
                .Select(member => (Type: type, Member: member)))
            .ToArray();

        var revisions = searchedSources
            .Select(searched => searched.Repo + "@" + searched.Ref + "@" + searched.Commit)
            .ToArray();
        var offset = DecodeCursor(cursor, "lookup", symbol, revisions);
        if (offset > pairs.Length)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = pairs.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < pairs.Length;

        var paged = page
            .GroupBy(pair => (pair.Type.FullName, pair.Type.Source.Repo))
            .Select(group => group.First().Type with
            {
                Members = group
                    .Select(pair => signaturesOnly ? ToSignature(pair.Member) : pair.Member)
                    .ToArray(),
            })
            .ToArray();

        return new ApiLookupResult(
            paged,
            searchedSources,
            outcome,
            distinctTypeNames,
            isPartial,
            isPartial ? EncodeCursor("lookup", symbol, nextOffset, revisions) : null);
    }

    private static ApiMemberDocumentation ToSignature(ApiMemberDocumentation member) =>
        new(member.Name, member.Signature, Summary: null, Parameters: null, Returns: null, Remarks: null);
```

Carry the answer out of the read instead of recomputing it. `ResolveSymbol` tries the whole symbol
as a type first — so `System.String` is a type and `String.Equals` is a member — and it already
knows which happened. Extend the record from Task 3:

```csharp
    private sealed record LookupRead(
        SourceProvenance Provenance,
        IReadOnlyList<ApiTypeDocumentation> Matches,
        IReadOnlyList<string> ResolvedTypeNames,
        bool SymbolNamedAMember);
```

and set the flag in `ReadLookupSource`, where `memberName` is in scope:

```csharp
        return new LookupRead(
            ToProvenance(definition, state),
            documented.Where(type => memberName is null || type.Members.Count > 0).ToArray(),
            documented.Select(type => type.FullName).ToArray(),
            memberName is not null);
```

The source loop from Task 3 must keep each read rather than only its parts, so declare
`var reads = new List<LookupRead>();` alongside the other accumulators and add `reads.Add(read);`
next to the existing `searchedSources.Add(read.Provenance);`.

The `Matches.Count > 0` guard matters: a source that holds nothing still reports a member name for
any dotted symbol, because resolution falls back to splitting on the last dot. Only a source that
actually produced matches gets a say in the detail level.

- [ ] **Step 6: Pass the new arguments through the tool and drop the indentation**

In `ApiDocsTool.cs`, change `WriteOptions`:

```csharp
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        // Indentation is roughly a fifth of every response's bytes and buys an agent nothing.
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
```

Change `LookupApi`'s signature and its call:

```csharp
    [McpServerTool(Name = "lookup_api", ReadOnly = true, Idempotent = true)]
    [Description(
        "Look up a .NET or Roslyn API type or member in synchronized ECMA XML docs. " +
        "TypeName returns every member's signature only; TypeName.MemberName returns full " +
        "documentation for that member. Pass source to restrict the lookup to dotnet-api-docs or " +
        "roslyn-api-docs, and limit/cursor to page. Returns provenance with every match.")]
    public static async Task<string> LookupApi(
        string symbol,
        ApiDocsQueryService service,
        CancellationToken cancellationToken,
        string? source = null,
        int? limit = null,
        string? cursor = null)
    {
        try
        {
            var result = await service.LookupAsync(
                symbol,
                source,
                limit ?? 20,
                cursor,
                cancellationToken).ConfigureAwait(false);
```

In `LookupApi`'s `catch (ArgumentException exception)` block, distinguish a cursor fault the way
`SearchApi` already does:

```csharp
        catch (ArgumentException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = string.Equals(exception.ParamName, "cursor", StringComparison.Ordinal)
                        ? "invalid_cursor"
                        : "invalid_request",
                    message = exception.Message,
                },
                WriteOptions);
        }
```

Do the same in `SourcesTool.cs` for its `WriteOptions`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --nologo`

Expected: PASS, 31 tests.

- [ ] **Step 8: Commit**

```bash
git add src/DotNetKnowledge.Mcp/Features/ \
        tests/DotNetKnowledge.Mcp.Tests/Features/ApiDocs/ApiDocsQueryServiceTests.cs
git commit -m "feat: budget lookup_api by symbol shape and paginate its members"
```

---

### Task 6: Report progress while a source synchronizes

A `dotnet-api-docs` clone takes about three minutes. A three-minute silence is indistinguishable
from a dead call; stage transitions are not. The client supplies a `progressToken`, so this needs
nothing experimental.

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Sources/SourceSynchronizer.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/Sources/SourcesTool.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/Sources/SourcesToolTests.cs`

**Interfaces:**
- Consumes: `GitCommandKind` from Task 2.
- Produces:
  - `SourceSynchronizer.SyncAsync(string name, string? requestedRef, CancellationToken cancellationToken, IProgress<string>? progress = null)`
  - `sync_source` accepts an injected `IProgress<ProgressNotificationValue>`.

The synchronizer reports plain stage names and does not depend on the MCP SDK; the tool translates
them into protocol notifications. That keeps `SourceSynchronizer` testable without a server.

- [ ] **Step 1: Write the failing test**

Add to `tests/DotNetKnowledge.Mcp.Tests/Features/Sources/SourcesToolTests.cs`:

```csharp
    [TestMethod]
    public async Task SyncAsyncReportsEveryStageInOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var repository = Path.Combine(root, "origin");

            // `docs`, not `xml/System`, and the source is named `local` below: this file's own
            // WriteCatalogAsync declares one source keyed "local" with sparse = ["docs"], and
            // SourceCatalog loads exactly what the catalog file holds with no defaults merged.
            // ApiDocsQueryServiceTests has a differently configured helper; do not mix them.
            var contentDirectory = Path.Combine(repository, "docs");
            Directory.CreateDirectory(contentDirectory);
            await RunGitAsync(null, "init", "--initial-branch=main", repository);
            await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
            await RunGitAsync(repository, "config", "user.name", "Tests");
            await File.WriteAllTextAsync(
                Path.Combine(contentDirectory, "Widget.xml"),
                "<Type Name=\"Widget\" FullName=\"System.Widget\" />");
            await RunGitAsync(repository, "add", ".");
            await RunGitAsync(repository, "commit", "-m", "docs");
            var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
            var catalogPath = Path.Combine(root, "sources.json");
            await WriteCatalogAsync(catalogPath, repository, pin);
            var synchronizer = new SourceSynchronizer(
                new SourceCatalog(catalogPath),
                new SourceCache(Path.Combine(root, "cache")));

            // A plain IProgress<T> that records inline. System.Progress<T> posts its callbacks
            // through the synchronization context, so a test using it would have to sleep before
            // asserting and would still be racy.
            var recorder = new RecordingProgress();
            await synchronizer.SyncAsync(
                "local",
                requestedRef: null,
                CancellationToken.None,
                recorder);

            CollectionAssert.AreEqual(
                new[] { "clone", "sparse-checkout", "fetch", "checkout", "validate" },
                recorder.Stages.ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }
```

Add the recorder as a private nested class on `SourcesToolTests`:

```csharp
    private sealed class RecordingProgress : IProgress<string>
    {
        public List<string> Stages { get; } = [];

        public void Report(string value) => Stages.Add(value);
    }
```

`SourcesToolTests` already defines `RunGitAsync`, `WriteCatalogAsync` and `DeleteDirectory` — reuse
them, do not add second copies.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter "FullyQualifiedName~ReportsEveryStage" --nologo`

Expected: FAIL to compile — `SyncAsync` takes three arguments.

- [ ] **Step 3: Thread the reporter through the synchronizer**

In `SourceSynchronizer.cs`, change `SyncAsync`:

```csharp
    public async Task<SourceSyncResult> SyncAsync(
        string name,
        string? requestedRef,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        if (!_catalog.TryGet(name, out var definition))
            throw new ArgumentException($"Unknown source '{name}'. Call list_sources to see valid names.", nameof(name));

        if (requestedRef is not null && !string.Equals(requestedRef, "head", StringComparison.Ordinal))
            throw new ArgumentException("ref must be omitted or \"head\".", nameof(requestedRef));

        await using var sourceLock = await AcquireLockAsync(name, cancellationToken).ConfigureAwait(false);
        return await SyncCoreAsync(name, definition, requestedRef, progress, cancellationToken).ConfigureAwait(false);
    }
```

Change `SyncCoreAsync`'s signature to accept `IProgress<string>? progress` before
`CancellationToken cancellationToken`, and report before the clone:

```csharp
            progress?.Report("clone");
            await GitCommandRunner.RunAsync(
                null,
                ["clone", "--filter=blob:none", "--no-checkout", "--sparse", "--quiet", "--", definition.Url, staging],
                GitCommandKind.Bulk,
                cancellationToken).ConfigureAwait(false);
            repositoryDirectory = staging;

            var commit = await SynchronizeRepositoryAsync(
                repositoryDirectory,
                definition,
                target,
                progress,
                cancellationToken).ConfigureAwait(false);
```

Change `SynchronizeRepositoryAsync` to accept `IProgress<string>? progress` before the cancellation
token, and report each remaining stage immediately before its command:

```csharp
        progress?.Report("sparse-checkout");
```
```csharp
        progress?.Report("fetch");
```
```csharp
        progress?.Report("checkout");
```
```csharp
        progress?.Report("validate");
```

placing `validate` before the `rev-parse` that begins the validation block.

- [ ] **Step 4: Translate stages into protocol notifications**

In `SourcesTool.cs`, add `using ModelContextProtocol;` and change `SyncSource`:

```csharp
    public static async Task<string> SyncSource(
        string name,
        SourceSynchronizer synchronizer,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken cancellationToken,
        string? @ref = null)
    {
        // Five known stages, so the client sees liveness with a real denominator rather than a
        // spinner. The SDK supplies a no-op reporter when the client sent no progress token.
        var stages = new[] { "clone", "sparse-checkout", "fetch", "checkout", "validate" };
        var completed = 0;
        var stageProgress = new Progress<string>(stage =>
        {
            completed++;
            progress.Report(new ProgressNotificationValue
            {
                Progress = completed,
                Total = stages.Length,
                Message = $"{name}: {stage}",
            });
        });

        try
        {
            var result = await synchronizer
                .SyncAsync(name, @ref, cancellationToken, stageProgress)
                .ConfigureAwait(false);
```

The rest of the method body is unchanged.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --nologo`

Expected: PASS, 32 tests.

- [ ] **Step 6: Commit**

```bash
git add src/DotNetKnowledge.Mcp/Sources/SourceSynchronizer.cs \
        src/DotNetKnowledge.Mcp/Features/Sources/SourcesTool.cs \
        tests/DotNetKnowledge.Mcp.Tests/Features/Sources/SourcesToolTests.cs
git commit -m "feat: report sync_source progress by stage"
```

---

### Task 7: Upgrade to ModelContextProtocol 2.0.0

Mechanical. Every API the server uses exists unchanged in 2.0.0, including
`ProgressNotificationValue` with the same `Progress`, `Total` and `Message` properties, which is why
Task 6 could safely precede this.

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj:29`

**Interfaces:**
- Consumes: everything from Tasks 1–6.
- Produces: no source change.

- [ ] **Step 1: Bump the package**

In `src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj`:

```xml
    <PackageReference Include="ModelContextProtocol" Version="2.0.0" />
```

Do **not** add `ModelContextProtocol.Extensions.Tasks`, and do not add a `NoWarn` — the experimental
`MCPEXP001`/`MCPEXP002` diagnostics belong to that package, which is deliberately not taken. See
`docs/decisions.md`.

- [ ] **Step 2: Build and run the whole suite**

Run: `dotnet build src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj --nologo`
Expected: 0 errors, 0 warnings.

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --nologo`
Expected: PASS, 32 tests.

- [ ] **Step 3: Smoke-test the server over real stdio**

A shell redirect swallows the server's stdout and looks like a server fault, so drive it from a
process that owns the pipes. `scripts/probes/README.md` records the requirement. Confirm
`initialize`, then `tools/list`, then `tools/call list_sources` all return.

- [ ] **Step 4: Commit**

```bash
git add src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj
git commit -m "chore: move to ModelContextProtocol 2.0.0"
```

---

### Task 8: Retire the fixed backlog items and record what was learned

**Files:**
- Modify: `README.md:89-107`
- Modify: `docs/design/mcp-tool-surface.md:11-20`
- Modify: `docs/backlog/README.md`
- Delete: `docs/backlog/git-subprocesses-hang-under-an-mcp-stdio-host.md`
- Delete: `docs/backlog/generic-members-are-unreachable-by-name.md`
- Delete: `docs/backlog/lookup-api-has-no-response-budget.md`
- Delete: `docs/backlog/a-non-generic-type-shadows-its-generic-namesake.md`
- Create: `docs/backlog/mcp-tasks-extension-is-not-adopted.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Delete the four resolved backlog files and their table rows**

```bash
git rm docs/backlog/git-subprocesses-hang-under-an-mcp-stdio-host.md \
       docs/backlog/generic-members-are-unreachable-by-name.md \
       docs/backlog/lookup-api-has-no-response-budget.md \
       docs/backlog/a-non-generic-type-shadows-its-generic-namesake.md
```

Remove the four matching rows from the table in `docs/backlog/README.md`. Documents state current
truth; `git log` is the record.

- [ ] **Step 2: Record why the Tasks extension is not adopted**

Create `docs/backlog/mcp-tasks-extension-is-not-adopted.md`, stating: the extension is
client-negotiated; `Optional` degrades to synchronous and `Required` refuses the call; this client
returns JSON-RPC error -32021 and sends only `claudecode/toolUseId` and `progressToken` in `_meta`;
the APIs carry `MCPEXP001`/`MCPEXP002` and track unratified SEP-2663. Note that
`scripts/probes/probe-mcp-host.cs` re-tests the premise in one call, and that adoption would be a
`WithTasks` call plus one execution mode. Add its row to `docs/backlog/README.md`.

- [ ] **Step 3: Correct the status prose**

In `README.md`, remove the paragraph stating the implemented tools do not work under an MCP client,
and the `sync_source` caveat in the "For agents" section. State that `list_sources`, `sync_source`,
`search_api` and `lookup_api` work, and that language-design and bundled-example queries remain
future work.

In `docs/design/mcp-tool-surface.md`, remove the paragraph recording the departure from the intended
surface, and update the `lookup_api` contract to describe the two detail levels and pagination.

- [ ] **Step 4: Add the rule the git fix created**

In `CLAUDE.md`, under "### Non-negotiables for every tool", add:

```markdown
- **Every git subprocess redirects standard input.** Git blocks during startup when it inherits a
  piped stdin handle from a parent whose own streams are pipes, which is what an MCP client
  creates — `git --version` included. `GitCommandRunner` does this; anything else that starts a
  process must too. `docs/gotchas.md` records the evidence.
```

- [ ] **Step 5: Append the decisions this cycle settled**

Add to `docs/decisions.md`, newest first, above the existing entries — the file is append-only, so
add without editing anything below:

- git timeout tiers named for duration rather than for the network, with `sparse-checkout` and
  `checkout` classified `Bulk` because they write the working tree; rejected: the spec's
  local/network split, which leaves both unclassified.

- [ ] **Step 6: Verify and commit**

```bash
dotnet scripts/verify-no-vendored-content.cs
dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --nologo
git add -A
git commit -m "docs: retire the fixed backlog items and record the tasks-extension deferral"
```

---

## Self-Review

**Spec coverage.** Git stdin → Task 1. Timeout tiers and the measurement gate → Task 2. Generic
member matching and the `not_found`/`member_not_found` split → Task 3. Shadowing → Task 4. Detail
levels, pagination, flat member sequence, top-level `isPartial`, `WriteIndented` → Task 5. Progress
notifications → Task 6. SDK 2.0.0 → Task 7. Fixture, control test, cursor rejection after
re-synchronization, provenance → Tasks 1, 2, 5. Documentation and the Tasks-deferral backlog file →
Task 8. No spec section is unimplemented.

**Two gaps found and closed while writing this.** The spec's local/network tier split does not
classify `sparse-checkout set` or `checkout --detach FETCH_HEAD`, both bulk working-tree writes;
Task 2 renames the tiers for duration and flags the deviation. And CI runs only the corpus suite, so
the spec's regression test would never have run in CI; Task 1 Step 9 adds the MCP suite.

**Type consistency.** `ApiLookupResult` is extended once in Task 3 (`Outcome`, `ResolvedTypeNames`)
and once in Task 5 (`IsPartial`, `NextPageToken`); both tasks show the full record. `LookupAsync`
gains `limit` and `cursor` in Task 5, and Task 5 Step 1 updates the Task 3 call sites.
`GitCommandRunner.RunAsync` gains `kind` and `timeouts` in Task 2, and Task 2 Step 6 updates the
fixture written in Task 1. `ApiMemberDocumentation.Parameters` becomes nullable in Task 5, which is
why `ToSignature` can omit it and why the Task 5 test dereferences it with `!`.
