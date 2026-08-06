# `sync_source`'s progress notifications have never been observed

`SourcesTool.SyncSource` reports five named stages — clone, sparse-checkout, fetch, checkout,
validate — through `StageReporter`, so a client sees liveness against a real denominator rather than
a spinner. Nothing has ever confirmed a notification arrives.

## Why it matters

`sync_source` is the one long-running tool here, and progress is the only thing distinguishing a slow
first clone of `dotnet-api-docs` from a hung one. If the notifications do not reach a client, the
failure is invisible in exactly the case they exist for: the caller waits, sees nothing, and cannot
tell which it is.

The stage count is also a claim. `StageReporter` is constructed with `stages.Length`, but each stage
is reported by `SyncCoreAsync` calling the reporter, so a stage that is skipped or reported twice
would produce a progress count that never reaches its total, or exceeds it.

This is deferred rather than fixed because the affected path works — sources sync correctly, and the
notification is advisory.

## Evidence

Absence of evidence, specifically. Smoke testing exercised `sync_source` against `vblang` twice at
the pin and once at `head`, and every payload assertion passed; the progress channel was not
observable through that client surface, so the case was left unchecked rather than recorded as
passing.

`scripts/probes/probe-mcp-host.cs` is the instrument that could settle it — it is the only one that
runs inside a real client — but it has no tool that sends a progress notification.

Related: [the MCP Tasks extension is not adopted](mcp-tasks-extension-is-not-adopted.md) defers task
support on the grounds that progress notifications cover the need. That argument rests on this
being true.

## Suggested fix

Add a probe tool to `probe-mcp-host.cs` that reports a known number of progress steps with a delay
between them, and observe whether the client surfaces them and in what order. That answers the
question for the host rather than for the server, which is where the doubt actually is.

Then assert the stage sequence directly in `tests/DotNetKnowledge.Mcp.Tests`, where a fake
`IProgress<ProgressNotificationValue>` can record what `SyncAsync` reports without a client at all.
The two together separate "the server reports five stages" from "this client shows them".
