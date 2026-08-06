# Gotchas

Facts that cost real time to establish and are not inferable from the code. Each is tagged
`environment` or `codebase`, because that is what decides when it needs re-checking: an environment
fact goes stale when a toolchain moves, a codebase fact when the code does.

**Append-only.** Never edit or delete an entry. A fact that turns out to be wrong is corrected by a
new entry naming the one it supersedes — a bare deletion loses the reason the wrong answer looked
right, which is the part that stops it being reached twice. Newest first. This preamble is not an
entry and may be revised.

This file is exempt from convention 3 in [`AGENTS.md`](../AGENTS.md).

**A gotcha is not a rule.** A rule is an obligation — always do this — and lives in
[`AGENTS.md`](../AGENTS.md) or [`CLAUDE.md`](../CLAUDE.md). A gotcha is a hazard: this may break, or
behave in a way you would not predict. A gotcha frequently justifies a rule, and then both exist and
the rule cites it. It does not become the rule, and it is not moved when one is written.

**Write an entry when** something behaved unexpectedly, cost more than about fifteen minutes to work
out, and the next reader would not infer it from the code.

**Four lines per entry**, linking to a spec or a [`docs/backlog/`](backlog/README.md) file if it
needs more.

---

### 2026-08-06 · A guard that walks up to `.git` passes silently from a worktree · codebase

`verify-project-namespaces.cs` tested `Directory.Exists(".git")` only. In a linked worktree `.git`
is a file, so the walk continued to the main checkout and every run scanned *that* tree — a seeded
violation in the worktree went unreported and the run still exited 0. The failure mode is a green
guard that measured someone else's code, which is worse than a red one.
`verify-no-vendored-content.cs`, `install-corpus-test-sdks.cs`, `verify-feature-floors.cs` and
`generate-net48-examples.cs` already tested both; only this one did not. Seed a violation and
confirm it is reported before trusting any new tree-walking guard.

### 2026-08-06 · `dotnet test` sets `DOTNET_HOST_PATH` itself · environment

The variable cannot be used to detect that a caller chose the repository-private host: `dotnet test`
exports it pointing at whichever host it is already running under, so under the machine host it
reads `C:\Program Files\dotnet\dotnet.exe` rather than being unset. A `RequiredToolchainsTests`
message keyed on its presence therefore announced the machine host as "the repository-private host".
Report the resolved path and let the reader recognize it; there is no reliable private/machine test,
because `--install-dir` relocates the private root anyway.

### 2026-08-05 · `git status` headroom is a property of the host, not of the command · environment

The same `git status --porcelain --untracked-files=all` over a synced `dotnet-api-docs` measured
0.101 s warm, 0.54 s cold, 3.34 s on a second machine, and **over 10 s** on that machine the first
time real-time anti-virus scanned the freshly written tree — failing a valid sync at the validate
stage and discarding 773 MB. Supersedes the "roughly 100x headroom" entry below, whose figure was a
single warm sample on one machine. It is now `GitCommandKind.Walk`; never re-tier it from one timing.

### 2026-08-05 · Quick-tier `git status` has roughly 100x headroom · environment

`git status --porcelain --untracked-files=all` against a fully synced `dotnet-api-docs`
(42,531 tracked files, ~700 MB working tree) measured 0.101 s warm — well inside the 10 s Quick
tier. This is a warm-cache figure; cold-cache behavior on a slower disk or under aggressive
anti-virus is unmeasured.

### 2026-08-05 · git hangs only when the parent is *reading* the inherited stdin · environment

A piped stdin alone does not hang git — measured at 34 ms. It hangs when the parent has an
outstanding read on the same handle, which an MCP stdio server always does because that read is the
transport. Supersedes the earlier 2026-08-05 entry, which named the pipe alone as the cause.
`RedirectStandardInput = true` remains the fix. Reproduce: `tests/DotNetKnowledge.Mcp.Tests.GitRunnerHost`.

### 2026-08-05 · git hangs when it inherits a piped stdin handle · environment

Git blocks during process startup, accumulating no CPU, when a parent with piped standard streams
lets it inherit stdin. `RedirectStandardInput = true` fixes it; closing is not required.
`git --version` hangs too, so it is not repository, network, or path related, and `cmd` under the
same handle is unaffected. Supersedes 2026-08-04. Reproduce: `scripts/probes/probe-mcp-host.cs`.

### 2026-08-05 · `JsonRpcMessage.Context` is null over stdio · environment

`ProtocolVersion`, `ClientInfo` and `ClientCapabilities` read from per-request metadata, which the
HTTP transports carry and stdio does not. Read the raw JSON-RPC request instead — that is what the
SDK itself inspects to decide whether a client declared an extension.

### 2026-08-05 · A running file-based app locks its own build output · environment

Rebuilding a `dotnet <file>.cs` program while a client has it launched fails with MSB3027 naming the
locked `.exe`. Disconnect or restart the client first. The error names a temp path under
`%LOCALAPPDATA%\Temp\dotnet\runfile\`, which does not obviously point back at the running server.

### 2026-08-05 · Semicolons in a file-based app directive must be escaped · environment

`#:property NoWarn=MCPEXP001%3BMCPEXP002`. An unescaped `;` does not reach MSBuild as a list
separator, and the suppression silently fails to apply.

### 2026-08-05 · `dotnet <file>.cs` writes nothing of its own to stdout · environment

Build and restore output goes elsewhere, including on a cold first run that compiles. This is what
makes a single-file C# program viable as an MCP stdio server, where stdout is the protocol channel.

### 2026-08-04 · git subprocess hang, mechanism unknown · environment

Every git subprocess hangs under an MCP stdio host. Inherited stdin ruled out by giving
`git rev-parse HEAD` a never-EOF stdin directly — a FIFO held open read-write — which exits 0
immediately. A FIFO is not a Windows pipe handle inherited through two process generations, and that
gap is why this rule-out was wrong.
