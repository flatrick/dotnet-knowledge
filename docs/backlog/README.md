# Backlog

One file per known issue or deferred decision. Each states a current condition, why it
matters, the evidence for it, and a suggested fix — not a history of how it was found.

An item lives here when it is real, understood, and deliberately not fixed yet. Delete the
file when the item is resolved; `git log` is the record.

| Item | Area | Why it is deferred |
|---|---|---|
| [The MCP Tasks extension is not adopted](mcp-tasks-extension-is-not-adopted.md) | server | Client-negotiated and unsupported by the target client; progress notifications cover the need |
| [No client has been observed rendering `sync_source`'s progress notifications](sync-source-progress-is-unverified.md) | server | The server emits them correctly; only the client's rendering is unknown, and it needs a person watching one |
| [The `exe` and `unsafe` projects under `CSharp/dotnet/` are in no build matrix](csharp-dotnet-exe-and-unsafe-projects-are-in-no-build-matrix.md) | corpus, tests | The exclusion is deliberate and asserted, but no recorded decision says why; the parallel net48 case was settled the other way |
| [A net48 row cannot carry a runtime claim](net48-rows-cannot-carry-a-runtime-claim.md) | corpus, tests | Extending the marker roots is cheap; whether a net48 case is runnable at all is the open part |
| [Glob resolution is implemented twice](glob-resolution-is-implemented-twice.md) | tooling, tests | The two agree; sharing code across the boundary is not possible |
