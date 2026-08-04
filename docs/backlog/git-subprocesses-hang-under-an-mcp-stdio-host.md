# Git subprocesses hang when the server runs under an MCP stdio host

Every `git` process `GitCommandRunner.RunAsync` starts is alive and idle indefinitely when the
server is launched as a stdio MCP server by a client. The process accumulates essentially no CPU
time and never exits, so `WaitForExitAsync` never returns and the tool call never completes.

It is not specific to a large repository, to the network, or to `clone`. `git rev-parse HEAD`
against a valid local checkout — a command that reads one ref file and does no I/O beyond it —
hangs the same way.

## Why it matters

This makes the server unusable for its purpose, and the failure gets worse as the server gets
closer to working:

- `sync_source` never completes, so no fetched source can be obtained through the server at all.
- Once a source *is* present in the cache, `list_sources` hangs too — `TryGetCurrentStateCoreAsync`
  shells out to `git rev-parse`, `git config` and `git status` to validate the checkout. Before any
  sync, `list_sources` answers normally, because `SourceCache.IsSynced` is a pure filesystem check
  and no git runs.
- Every query tool routes through `ReadCurrentSourceAsync` → `TryGetCurrentStateCoreAsync`, so
  `lookup_api` and `search_api` inherit the same hang.

The result is a server that answers only while it has nothing to answer with. A client sees a tool
call that never returns rather than an error, so nothing in the response surface indicates a fault.

## Evidence

Reproduced three times against `0.1.0-preview.1.gc284699ad4` on Windows 11, Git for Windows,
with the tool installed user-globally and launched by an MCP client as `dotnet-knowledge`:

| Invocation | Result |
|---|---|
| `sync_source(name: "roslyn-api-docs")` | no completion; cancelled after more than 7 minutes |
| `sync_source(name: "roslyn-api-docs")`, repeated | no completion; cancelled after more than 2 minutes |
| `list_sources` with `roslyn-api-docs` present in the cache | no completion; cancelled after more than 2 minutes |

Each figure is the interval to cancellation, not a measured limit — none of the three ever returned.

In every case the process tree is `cmd\git.exe` → `mingw64\bin\git.exe`, both idle:

```
ProcessId 4668  ParentProcessId 15056  "git" rev-parse HEAD        CPU 0,00
ProcessId 31048 ParentProcessId 4668   git.exe rev-parse HEAD      CPU 0,02
```

CPU does not move across repeated samples. During the two `clone` attempts no `git-remote-https`
child ever appears and the staging directory `.roslyn-api-docs-<guid>.tmp` is never created, so the
clone does not reach its network stage.

The same commands run outside the server complete immediately. Replaying the exact sequence
`SyncCoreAsync` performs — `clone --filter=blob:none --no-checkout --sparse --quiet`,
`sparse-checkout set`, `fetch --depth 1`, `checkout --detach FETCH_HEAD` — from a shell, into the
same `%LOCALAPPDATA%` staging path and against the same pins:

- `roslyn-api-docs` — 19 s, 306 MB
- `dotnet-api-docs` — 2 min 57 s, 806 MB

Cancellation behaves correctly: `process.Kill(entireProcessTree: true)` removes the whole tree and
leaves no staging directory behind.

`SourceSynchronizerTests` does not catch this. Its clones run under `dotnet test`, a plain console
host, where git behaves normally.

### Ruled out

- **Network, URL, and repository size.** A manual clone of the same URL with the same flags
  succeeds in seconds.
- **The destination path.** A manual clone into the identical
  `%LOCALAPPDATA%\dotnet-knowledge\sources\` staging directory succeeds.
- **Credentials.** No credential-helper process appears, and `GIT_TERMINAL_PROMPT=0` is already set.
- **An inherited stdin pipe that never reaches EOF.** `GitCommandRunner` does not set
  `RedirectStandardInput`, so the child inherits the host's stdin. Giving `git rev-parse HEAD` a
  never-EOF stdin directly — a FIFO held open read-write, with no other process in the pipeline —
  still exits 0 immediately.
- **Git's `cmd\git.exe` shim.** Invoking `mingw64\bin\git.exe` directly behaves identically to the
  shim outside the server.

The mechanism is therefore still open. What is established is the boundary: the same git commands,
against the same repositories and paths, succeed from a console process and hang from the
MCP-hosted one.

## Suggested fix

Diagnose against the hung child rather than the caller — a native stack or wait-chain for the idle
`git.exe` will name what it is blocked on, which no amount of variation at the call site has
revealed.

Two changes are worth making regardless of the mechanism, because both convert an unbounded hang
into a diagnosable failure:

- Redirect standard input and close it immediately, so the child can never inherit a live handle
  from the host and can never wait on one.
- Give `GitCommandRunner.RunAsync` a timeout that kills the process tree and reports which git
  command exceeded it. A tool that fails in 30 seconds naming the command is recoverable; one that
  hangs is not.

Add a test that exercises the runner from a process whose standard streams are pipes rather than a
console, since that is the only configuration in which the fault appears.
