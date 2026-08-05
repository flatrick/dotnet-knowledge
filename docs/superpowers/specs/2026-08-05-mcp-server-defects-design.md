# MCP Server Defect Remediation Design

## Purpose

Make the MCP server usable from an MCP client and correct three defects in its API-documentation
query surface. The server currently answers only while it has nothing to answer with: every `git`
subprocess it starts under a client hangs, so `sync_source` never completes and, once a source is
cached, `list_sources` and both query tools hang as well. The three query defects sit behind that
blocker and are invisible until it lifts.

## Scope

Six changes, in one implementation cycle:

- generic members reachable by name in `lookup_api`;
- a non-generic type no longer shadowing its generic namesake;
- a response budget for `lookup_api`;
- the git subprocess hang;
- progress reporting for long synchronizations; and
- the `ModelContextProtocol` package upgrade from 1.3.0 to 2.0.0.

It excludes the language-design and bundled-example tools, which remain future work, and the MCP
Tasks extension, which is deferred with recorded evidence rather than built.

## Sequencing

The git fix lands first. It is a one-line change, and it is what makes the other five observable
through a client at all; verifying a query fix against a server whose every call hangs means
verifying it only under `dotnet test`. The query defects follow in the order they appear above, then
progress reporting, then the package upgrade.

## Git subprocess standard input

`GitCommandRunner.RunAsync` sets `RedirectStandardInput = true` and closes the stream immediately
after `Process.Start()`.

The fault is not the MCP host, the repository, the network, the destination path, or Git for
Windows' `cmd\git.exe` shim. The single deciding variable is whether the child inherits a piped
standard input handle from its parent. Measured through `scripts/probes/probe-mcp-host.cs`, driven
by a plain .NET parent with piped standard streams and no MCP client involved:

| Command | Standard input | Result |
|---|---|---|
| `git --version` | inherited | hang, killed at 8 s |
| `git --version` | redirected, left open | exit 0, 29 ms |
| `git --version` | redirected, closed | exit 0, 24 ms |
| `git rev-parse HEAD` | inherited | hang, killed at 8 s |
| `git rev-parse HEAD` | redirected, left open | exit 0, 28 ms |
| `git rev-parse HEAD` | redirected, closed | exit 0, 27 ms |
| `git status --porcelain` | redirected, closed | exit 0, 79 ms |
| `cmd /c echo hello` | inherited | exit 0, 30 ms |

`git --version` opens no repository, reads no ref, and touches no network, so this is git's process
startup rather than any work it was asked to do. `cmd` under the same inherited handle is
unaffected, so it is not a property of subprocesses generally.

Redirecting alone is sufficient. The stream is closed anyway: it costs nothing and removes the
possibility of a future git invocation blocking on a handle that never reaches end of file.

The underlying mechanism is not established. The supported hypothesis is that the MSYS2 runtime
probes its inherited standard input handle during startup and blocks on a handle it cannot classify.
The remediation does not depend on confirming it — the boundary is sharp and reproducible.

### Timeout tiers

`RunAsync` takes a `GitCommandKind` and applies a timeout accordingly:

- **`GitCommandKind.Local`** — `rev-parse`, `config`, `status`. Ten seconds.
- **`GitCommandKind.Network`** — `clone`, `fetch`. Fifteen minutes, roughly five times the measured
  worst case: `dotnet-api-docs` clones in 2 min 57 s for 806 MB, and the margin covers a slower link
  without leaving a genuinely stuck clone to run indefinitely.

On expiry the process tree is killed and the failure names the git command that exceeded its tier.
An unbounded hang is unrecoverable; a failure in ten seconds that names `git status` is not.

One measurement gates the local tier before it is pinned: `git status --porcelain
--untracked-files=all` against a fully synchronized `dotnet-api-docs`, with a cold page cache. That
command walks the entire sparse working tree, and a timeout that fires on a healthy repository is a
worse defect than the one being fixed. If the measurement lands near ten seconds, the tier rises.

## Generic member resolution

`ApiDocsQueryService.ReadType` matches a requested member against the `MemberName` attribute
truncated at its first `<`, and also accepts the fully-specified form verbatim. `Select` matches
`Select<TSource,TResult>`; `FirstAncestorOrSelf` matches both `FirstAncestorOrSelf<TNode>` and
`FirstAncestorOrSelf<TNode,TArg>`.

Every arity of a requested name is returned. Overloads that differ only in type parameters are what
a caller asking for `Select` wants to see, and there is no cheaper way for them to discover which
arities exist.

### Distinguishing a missing type from a missing member

`ResolveSymbol` already knows whether the type portion of a symbol resolved. That knowledge reaches
the tool rather than being discarded: `ApiLookupResult` carries an outcome of `Found`,
`TypeNotFound`, or `MemberNotFound`, and in the last case the names of the types that did resolve.

`ApiDocsTool.LookupApi` maps these to two distinct errors:

- `not_found` — the type did not resolve. The remedy stays "call `search_api` with a type-name
  fragment to find candidates."
- `member_not_found` — the type resolved and the member did not. The response names the resolved
  types, and the remedy is "call `lookup_api` with just the type name to list its members."

Today that second remedy would be unusable advice, because a bare type name returns every member in
full. It becomes honest only alongside the response budget below. Directing a caller to `search_api`
for a member is wrong in either case: `ReadSearchSource` enumerates file names and never opens a
document, so no `search_api` call can surface a member.

## Generic type shadowing

`FindTypeFilesInNamespace` returns the exact `<simpleName>.xml` **and** the matches of the
`` <simpleName>`*.xml `` glob, rather than returning the exact match alone whenever it exists. The
exact non-generic match is ordered first, with generic arities following in ordinal order.

`SyntaxList` returns both `Microsoft.CodeAnalysis.SyntaxList`, a static helper with one method, and
`Microsoft.CodeAnalysis.SyntaxList<TNode>`, the collection that appears throughout the Roslyn API
surface. No new response shape is required: `lookup_api` already returns several matches for one
name.

The current behavior fails silently. It is a successful lookup naming a real type with real members,
so nothing signals that a larger type of the same name was passed over.

## `lookup_api` response budget

The shape of the requested symbol selects the detail level. There is no new parameter:

- **a bare type name** returns each member's `name` and `signature` only — no summary, no parameter
  documentation, no returns text, no `remarks`;
- **`Type.Member`** returns full documentation including `remarks`.

This makes the expensive response unreachable rather than merely opt-out, and it matches the
two-step narrowing `docs/design/mcp-tool-surface.md` already argues for. `remarks` is the single
largest contributor to response size and is frequently a long prose block; it appears only when a
caller has named the member it belongs to.

Pagination mirrors `search_api`: a `limit` between 1 and 100 defaulting to 20, and an opaque
`cursor`. Both apply in either detail mode. The cursor encodes a version, the symbol, an offset, and
the searched source revisions, so a cursor cannot survive a re-synchronization that changes what it
points at.

Paging is over a single flat sequence of `(type, member)` pairs across all matches, ordered by type
full name, then member name, then signature — all ordinal, so the order is stable across calls.
`isPartial` and `nextPageToken` sit at the top level of the response, not inside each match;
per-match cursors would give a three-type result such as `List` three independent pagination states
with no way to combine them.

`WriteIndented` becomes `false`. Indentation is roughly a fifth of every response's bytes and buys
an agent caller nothing.

Current cost, for reference — the figures this budget exists to remove:

| Symbol | Members | Wire bytes |
|---|---|---|
| `String` | 235 | 427 817 |
| `List` | 82 across 3 types | 183 005 |
| `Workspace` | 167 across 2 types | 81 955 |

## Long synchronization feedback

`sync_source` reports progress across its five stages — clone, sparse-checkout, fetch, checkout,
validate — through MCP progress notifications.

This works today and needs nothing experimental. The client supplies a `progressToken` on the
request, confirmed by the `_meta` dump from `probe_host`. A three-minute clone that reports stage
transitions is distinguishable from a dead call; one that reports nothing is not.

## Package upgrade

`ModelContextProtocol` moves from 1.3.0 to 2.0.0. Every API the server uses — `AddMcpServer`,
`WithStdioServerTransport`, `WithToolsFromAssembly`, and the `McpServerTool` attribute's `Name`,
`ReadOnly` and `Idempotent` properties — exists unchanged in 2.0.0. No behavior rides on the
upgrade, and it needs no `NoWarn`, because the experimental diagnostics belong to the Tasks
extension package that is not being taken.

## Testing

Three tests over the query surface currently use synthetic fixtures, `System.AlphaWidget` and
`System.BetaWidget`, whose XML carries no generic member and no shadowing pair. Both are added to
the fixtures.

Query coverage:

- a generic member is returned for its plain name, and every arity of that name is returned;
- a fully-specified generic member name still matches;
- `member_not_found` is returned, distinct from `not_found`, when the type resolves and the member
  does not, and it names the resolved types;
- a bare type name returns no `remarks` and no summaries, and paginates;
- `Type.Member` returns `remarks`;
- a generic type is reachable by its plain name when a non-generic sibling exists, with the
  non-generic match ordered first;
- a cursor issued against one source revision is rejected after a re-synchronization; and
- the provenance envelope — `repo`, `ref`, `commit`, `fetchedAt` — is present in every new response
  shape.

Git coverage requires a parent process whose standard streams are pipes, which is the only
configuration in which the fault appears and the reason `SourceSynchronizerTests` never caught it:
`dotnet test` is a console host where git behaves normally.

A console fixture project, `tests/DotNetKnowledge.Mcp.Tests.GitRunnerHost/`, references the server
project and calls `GitCommandRunner.RunAsync` directly. `GitCommandRunner` is `internal`, so the
server project grants the fixture `InternalsVisibleTo`. The call must go through the real runner
rather than a copy of its `ProcessStartInfo`, or the test verifies the copy and not the fix.

Two tests launch the fixture with all three standard streams redirected:

- **the fix** — the runner completes `git --version` well inside the local tier.
- **the control** — the fixture, on a second switch, starts git from a raw `ProcessStartInfo` that
  inherits standard input, and does not complete. This deliberately bypasses the runner: its job is
  to prove the harness reproduces the fault, so that the first test passing means something. Without
  it, a first test that passes for an unrelated reason is indistinguishable from a fixed defect.

The control costs its timeout on every run. That is the price of a regression test known to be
capable of failing.

## Documentation

- `README.md` — the status section drops the statement that the implemented tools do not work under
  an MCP client.
- `docs/design/mcp-tool-surface.md` — the `lookup_api` contract gains its detail levels and
  pagination; the paragraph recording the departure from the intended surface is removed.
- `docs/backlog/` — the four defect files and their rows in `docs/backlog/README.md` are deleted as
  each fix lands. Documents state current truth; `git log` is the changelog.
- `docs/backlog/` gains one file recording why the MCP Tasks extension is not adopted.

## Deferred: the MCP Tasks extension

`ModelContextProtocol.Extensions.Tasks` 2.0.0 provides `tasks/get`, `tasks/update` and
`tasks/cancel`, a `WithTasks` builder call, an `InMemoryMcpTaskStore` suited to a long-lived stdio
process, and per-tool `McpTaskExecutionMode` selection. It is not adopted, for reasons that are
measured rather than assumed:

- The extension is client-negotiated. `Optional` degrades to plain synchronous execution against a
  client that does not declare `io.modelcontextprotocol/tasks`, and `Required` refuses the call
  outright.
- The client does not declare it. `probe_task_required`, a tool in `Required` mode, returns JSON-RPC
  error -32021, "The request requires the 'io.modelcontextprotocol/tasks' client extension
  capability." The `_meta` of an ordinary tool call carries only `claudecode/toolUseId` and
  `progressToken`.
- The APIs carry `MCPEXP001` and `MCPEXP002` experimental diagnostics, so adopting them requires the
  server project's first warning suppression, and they track SEP-2663, which is not a ratified
  protocol revision — the wire shape can still change.

Progress notifications cover the need this would have served. If client support arrives, the change
is a `WithTasks` call and one execution mode on `sync_source`; `scripts/probes/probe-mcp-host.cs`
re-tests the premise in a single call.
