# No client has been observed rendering `sync_source`'s progress notifications

`SourcesTool.SyncSource` reports five named stages — clone, sparse-checkout, fetch, checkout,
validate — through `StageReporter`. The server emits them correctly and they reach the wire; what
remains unknown is whether the MCP client this server is used from puts them in front of anyone.

## Why it matters

`sync_source` is the one long-running tool here, and progress is the only thing distinguishing a slow
first clone of `dotnet-api-docs` from a hung one. If a client discards the notifications, the failure
is invisible in exactly the case they exist for: the caller waits, sees nothing, and cannot tell
which it is.

[The MCP Tasks extension is not adopted](mcp-tasks-extension-is-not-adopted.md) defers task support
on the grounds that progress notifications cover the need. The server half of that argument holds;
the client half is what this item still leaves open.

## Evidence

Settled, on the server side:

- `SyncAsyncReportsEveryStageInOrder` asserts the exact stage sequence `SyncCoreAsync` reports.
- `SyncSourcePublishesProgressNotificationsInStageOrder` asserts that `StageReporter` turns it into
  five notifications, one per stage, each naming its stage, with the running count reaching the
  denominator exactly. Both fail when a stage is reported twice.
- Driving the built server over redirected stdio against `vblang` produced five
  `notifications/progress` messages carrying the caller's `progressToken`, in stage order, all before
  the `tools/call` result:

  ```
  progress 1 of 5  "vblang: clone"
  progress 2 of 5  "vblang: sparse-checkout"
  progress 3 of 5  "vblang: fetch"
  progress 4 of 5  "vblang: checkout"
  progress 5 of 5  "vblang: validate"
  ```

Open, on the client side: no client has been watched receiving them. Smoke testing exercised
`sync_source` against `vblang` twice at the pin and once at `head`, and every payload assertion
passed, but the progress channel was not observable through that surface.

## Suggested fix

`probe_progress` in [`scripts/probes/probe-mcp-host.cs`](../../scripts/probes/probe-mcp-host.cs) is
the instrument. Point a client at that probe and call it — it sends a known sequence with a delay
between steps and returns the same sequence, so what the client rendered can be diffed against what
was sent. Its `progressToken` field separates the two ways a client can show nothing: a null token
means the client never asked for progress, and the SDK handed the tool a no-op reporter; a non-null
token with nothing rendered means the client received the notifications and dropped them.
