# The MCP Tasks extension is not adopted

`ModelContextProtocol.Extensions.Tasks` 2.0.0 provides `tasks/get`, `tasks/update` and
`tasks/cancel`, a `WithTasks` builder call, an `InMemoryMcpTaskStore` suited to a long-lived stdio
process, and per-tool `McpTaskExecutionMode` selection — a natural fit for `sync_source`, whose
`Bulk`-tier git commands can run for minutes. It is not wired in.

## Why it matters

The extension is client-negotiated, and both of its execution modes fail against a client that has
not opted in: `McpTaskExecutionMode.Optional` silently degrades to plain synchronous execution, so a
tool marked `Optional` behaves exactly as if the extension were never added, and
`McpTaskExecutionMode.Required` refuses the call outright rather than degrading. Neither mode gives
`sync_source` a working asynchronous path against a client that does not declare the extension.

## Evidence

The target client does not declare `io.modelcontextprotocol/tasks`. `scripts/probes/probe-mcp-host.cs`
runs one tool, `probe_task_required`, in `Required` mode; against this client it returns JSON-RPC
error **-32021**, "The request requires the 'io.modelcontextprotocol/tasks' client extension
capability." The `_meta` of an ordinary tool call carries only `claudecode/toolUseId` and
`progressToken` — no task-extension field of any kind.

Separately, the APIs themselves carry `MCPEXP001`/`MCPEXP002` experimental diagnostics and track
**SEP-2663**, which is not a ratified protocol revision — the wire shape can still change under a
package the server would otherwise take a hard dependency on.

## Suggested fix

Nothing to fix today. `sync_source` reports progress through ordinary MCP progress notifications,
which covers the need this extension would have served. Re-test the premise with a single call —
`scripts/probes/probe-mcp-host.cs`'s `probe_task_required` tool — before revisiting; if a future
client declares the extension, adoption is a `WithTasks` call plus one `McpTaskExecutionMode` on
`sync_source`.
