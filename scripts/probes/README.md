# Probes

Throwaway MCP servers that answer questions about the *host*, not about this repository's code.

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

## probe-mcp-host.cs

| Tool | Question it answers |
|---|---|
| `probe_host` | What did the client negotiate, and how are this process's standard streams wired? Dumps the raw request, so a client extension declaration is visible if present. |
| `probe_process` | Does a child process complete? Runs one command under a hard timeout, with `stdinMode` selecting whether the child inherits the host's stdin. `killOnTimeout: false` leaves a hung child alive for external inspection. |
| `probe_task_required` | Does this client declare `io.modelcontextprotocol/tasks`? Runs in `Required` mode, so support is a result and absence is JSON-RPC error -32021. |
| `probe_sleep` | What does this client do with a call that outlives its patience — cancel, time out, or wait? |

`probe_host`'s `protocolVersion`, `clientInfo` and `clientCapabilities` read as `null` over stdio.
They are populated from per-request metadata, which the HTTP transports carry and stdio does not;
the `rawRequest` dump is the reliable field.

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
