# Probes

Throwaway programs that answer questions about the *host*, not about this repository's code. Most
are MCP servers; six are not — two drivers that play the client, one that interrogates the
machine's compilers, and three that run shipped package-pipeline code over real NuGet content.

Some faults only appear when a server is launched by an MCP client, and a client is an awkward place
to run an experiment: the tool surface is fixed, output is summarized, and a hung call looks the same
as a slow one. A probe is a minimal server carrying one question, so the answer arrives as a value
rather than as an inference.

These are diagnostic instruments, not part of the shipped server. Nothing in
`src/DotNetKnowledge.Mcp/` references them, and they are free to depend on experimental packages the
production server will not take.

## Running one

A probe is a single-file C# program, so a client launches it directly:

```json
{
  "mcpServers": {
    "probe": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["C:\\src\\github\\flatrick\\dotnet-knowledge\\scripts\\probes\\probe-mcp-host.cs"]
    }
  }
}
```

The first launch compiles; later launches reuse the build. `dotnet build scripts/probes/<name>.cs`
warms it, but a running server holds a lock on its own output, so reconnect the client before
rebuilding after an edit.

Driving a probe from a shell needs a parent process that owns the pipes. A Git Bash `>` redirect
swallows the server's stdout entirely and reads as a server fault.
`drive-probe-progress.cs` and `drive-sync-progress.cs` are that parent process.

## probe-mcp-host.cs

| Tool | Question it answers |
|---|---|
| `probe_host` | What did the client negotiate, and how are this process's standard streams wired? Dumps the raw request, so a client extension declaration is visible if present. |
| `probe_process` | Does a child process complete? Runs one command under a hard timeout, with `stdinMode` selecting whether the child inherits the host's stdin. `killOnTimeout: false` leaves a hung child alive for external inspection. |
| `probe_task_required` | Does this client declare `io.modelcontextprotocol/tasks`? Runs in `Required` mode, so support is a result and absence is JSON-RPC error -32021. |
| `probe_sleep` | What does this client do with a call that outlives its patience — cancel, time out, or wait? |
| `probe_progress` | Does this client surface progress notifications, and in what order? Sends a known sequence with a delay between steps and returns that sequence, so what the client rendered can be diffed against what was sent. `progressToken: null` means the client asked for no progress at all, and `reporterType` names the object that received the reports — which is how a silent client is told from a silent server. |

`probe_host`'s `protocolVersion`, `clientInfo` and `clientCapabilities` read as `null` over stdio.
They are populated from per-request metadata, which the HTTP transports carry and stdio does not;
the `rawRequest` dump is the reliable field.

## drive-probe-progress.cs

Not a server — the client.
It starts `probe-mcp-host.cs` with redirected stdio, speaks enough JSON-RPC to call `probe_progress`
with a progress token, and prints every server→client frame with the millisecond it arrived at.

It answers what a compliant client sees on the wire: how many notifications, in what order, with
what spacing.
That is the reference the same call under a real MCP client is diffed against, which is how a client
that drops progress is told from a server that never sent any.

**It cannot say anything about a real client's rendering, and it is not evidence about the shipped
server** — `probe-mcp-host.cs` shares none of `DotNetKnowledge.Mcp`'s code.

```bash
dotnet run --file scripts/probes/drive-probe-progress.cs
dotnet run --file scripts/probes/drive-probe-progress.cs -- --steps 6 --delay 100
```

## drive-sync-progress.cs

The same shape against the **real** server: it starts the built `DotNetKnowledge.Mcp` with
redirected stdio and calls `sync_source` with a progress token.
This is the only place the five sync stages — clone, sparse-checkout, fetch, checkout, validate —
are visible as raw `notifications/progress` frames, and it is what established that they reach the
wire at all.

The cache is an isolated directory under the checkout's `.scratch/`, set through
`DOTNET_KNOWLEDGE_CACHE`, so a run never touches the per-user source cache.
It needs a built server — `dotnet build src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj` — and
says so rather than failing obscurely.
It reaches a real remote, so it is not an offline instrument.

**It cannot say what any real client does with those notifications.** Use `probe_progress` through a
client for that.

```bash
dotnet run --file scripts/probes/drive-sync-progress.cs
dotnet run --file scripts/probes/drive-sync-progress.cs -- --source csharplang
```

## probe-sync.cs

Not an MCP server. A plain console program that runs the five stages of
`SourceSynchronizer.SyncCoreAsync` — clone, sparse-checkout, fetch, checkout, validate — with the
same argv, environment, stream redirection and per-stage timeout tiers as `GitCommandRunner`, timing
each stage and flagging any `Quick`-tier command past half its ceiling. Unlike the server it never
deletes its staging directory, so a failed run leaves the downloaded tree on disk for inspection.

**It cannot reproduce any fault that requires the client.** Run from a terminal, stdin is a console
and no parent is reading it, so the piped-stdin hang below cannot occur and no client-side timeout
applies. A green run establishes that git, the network and the disk are healthy on that machine —
and nothing at all about how the same commands behave under a stdio host. Use `probe-mcp-host.cs`
for that; it is the only instrument here that runs inside a real client.

`probe_process` established that git hangs indefinitely when it inherits a piped stdin handle —
`git --version` included, so it is git's startup rather than any repository work — and completes in
about 30 ms when standard input is redirected. That is the whole of the fault that made every
synchronized-source tool hang under a client.

## probe-preferreduilang.cs

Not an MCP server either — a probe of the *host's compilers*.
It runs each of the nine compiler binaries this repository drives over a file that errors and a file
that does not, at baseline and under two spellings of `/preferreduilang`, then tries the `VSLANG`
and `DOTNET_CLI_UI_LANGUAGE` routes for the ones that reject the switch.

It answers whether `/preferreduilang` support is a property of the generation, of the language, or
of the individual binary.
The measured answer is the binary: `v4.0.30319` ships a `csc` that honors the switch beside a `vbc`
that warns `BC2007` and ignores it, from the same directory.
That is the fact behind the corpus's per-compiler `HonorsPreferredUiLanguage` flag (in
`verify-feature-floors.cs`, now in [flatrick/dotnet-code-examples](https://github.com/flatrick/dotnet-code-examples)),
and `docs/gotchas.md` carries the full table.

**It cannot separate "the compiler honored the switch" from "this machine has no satellite for the
current UI language."**
A compiler that already prints English says nothing either way; the printed baseline line is what
shows which case a row is, and that is a fact about the machine rather than the binary.

The `Microsoft.Net.Compilers` 1.3.2 pair comes from `.artifacts/period-compilers/`, which the
corpus's `verify-feature-floors.cs` downloads; the modern pair is found with the same `vswhere`
query that script uses.
Missing binaries are reported, not guessed at. Windows only.

```bash
dotnet run --file scripts/probes/probe-preferreduilang.cs
```

## probe-api-package-supplement.cs

Not an MCP server. It runs the **shipped** package pipeline — `PackageArchiveReader.ReadAssets`,
`PackageApiCorpusBuilder.BuildAsync`, `PackageApiCorpusStore.Read` — over one `.nupkg` the operator
already has on disk, and prints the package id and version, the SHA-512 it computed with whether
that matches the catalog's pin, the frameworks found and built, the cataloged default, and
`MSBuildWorkspace`'s member count and `Create` overload signatures.

It answers whether the real `Microsoft.CodeAnalysis.Workspaces.MSBuild` package has the layout,
assembly names and documentation IDs the server assumes. The automated suite exercises the same code
against repository-authored fixtures, which cannot answer that: a fixture is built to the
assumption rather than against it.

**It proves compatibility with one local copy of one package at one version, and nothing else.** It
never downloads, so it says nothing about the NuGet client, the feed, or hash verification on the
wire; and nothing about layouts a future package version might ship. Those stay covered by the
offline tests and by re-running this probe when the pin moves.

```powershell
dotnet run --file scripts/probes/probe-api-package-supplement.cs -- --package "$env:USERPROFILE\.nuget\packages\microsoft.codeanalysis.workspaces.msbuild\5.3.0\microsoft.codeanalysis.workspaces.msbuild.5.3.0.nupkg"
```

## diff-tfm-surface.cs

Not an MCP server. It runs the shipped `MetadataApiReader.Read` over every
`lib/<framework>/<assembly>.dll` of one extracted package and diffs the public type and member sets
pairwise, printing each pair as `IDENTICAL` or with the declarations that differ. It exits 1 when any
pair differs, so it can be run over a set of packages and the interesting one picked out by exit
code.

It answers whether a package's public API surface actually differs between its target frameworks —
the question the server's `framework` argument exists to serve. A compiler XML file that differs per
framework does not settle it, because the difference is routinely internal and never reaches a
public-API corpus.

**It says nothing about any package it is not run against**, which is the whole of its limitation:
the finding it supports is a claim about packages in general drawn from the few measured so far.
`docs/backlog/framework-selection-has-no-observable-effect.md` carries those measurements.

```powershell
dotnet run --file scripts/probes/diff-tfm-surface.cs -- --package "$env:USERPROFILE\.nuget\packages\microsoft.codeanalysis.common\5.6.0" --assembly Microsoft.CodeAnalysis
```

## sweep-api-reader.cs

Not an MCP server. It runs the shipped `MetadataApiReader.Read` over every `lib/<framework>/*.dll` in
a package folder tree — the machine's NuGet cache by default — and reports how many assemblies it
reads, how many it refuses, and the refusal reasons grouped with the declaration names collapsed
out. It exits 1 when anything was refused.

It answers whether the set of metadata shapes the reader does not model is small and nearly closed
or open-ended, which is what decides between fixing shapes one at a time and making an undecodable
declaration stop costing the whole package.
`docs/backlog/undecodable-metadata-fails-the-whole-package.md` carries the measurement.

**The rate is a property of whatever that machine happens to have restored**, not of NuGet as a
whole, and a cache full of one ecosystem's packages will read as that ecosystem. The first-party
line is broken out separately because those are the packages the server would realistically catalog.

```powershell
dotnet run --file scripts/probes/sweep-api-reader.cs
dotnet run --file scripts/probes/sweep-api-reader.cs -- --root D:\packages --limit 200
```
