# MCP session source handshake design

## Goal

Make every agent learn, from the MCP server's own tool metadata and its first response, that it
must inspect source availability at the start of each MCP connection before using any other
`dotnet-knowledge` tool. The response must say exactly when to call `sync_source` and provide the
arguments needed to do so.

This is an onboarding contract for MCP callers. It does not change installation guidance for people
developing the server.

## Contract

`list_sources` is the mandatory session-start handshake. Its tool description instructs an agent to
call it before every other `dotnet-knowledge` tool call on a new MCP connection. The reason is
explicit: a source that was usable in an earlier session can be removed or become invalid before
the next one.

`list_sources` remains read-only. It does not synchronize, repair, or otherwise change source
state. `sync_source` remains the only operation that fetches and writes source state.

## Response shape

Each source result includes a machine-readable `nextAction`:

- A source whose `synced` value is false has
  `{ "tool": "sync_source", "arguments": { "name": "<source name>" } }`.
- A source whose `synced` value is true has `{ "tool": null }`, which means its corresponding
  query tools are ready to use.

The top-level `nextStep` is always present and gives the session rule once: inspect the chosen
source's `nextAction`; call `sync_source` only when it names that tool; omit `ref` to fetch the
server-vouched pinned revision; pass `ref: "head"` only when deliberately accepting upstream
drift.

This replaces the current top-level message that only names the first unsynced source. An agent
must not infer that a different unsynced source is ready merely because it was not the one named.

## Failure behavior

The handshake is a snapshot, not a lock. Existing query-tool errors remain the fallback when a
source disappears or becomes invalid after `list_sources` returns; they direct the caller to
`sync_source`. A later source change must not produce a silent empty result.

## Implementation boundaries

The change is limited to the source tool's public tool description and serialized response model,
the MCP tool-surface documentation, and tests. It adds no MCP tool, prompt, resource, source
cache behavior, or implicit synchronization.

## Verification

Tests prove that:

- `tools/list` advertises `list_sources` as the command required at the start of each MCP session.
- An unsynced source returns the exact `sync_source` action and source-name argument.
- A valid synchronized source returns the ready action.
- The top-level session rule is present for both states.
- The existing redirected-stdio test observes the public metadata and response over a real MCP
  connection.

