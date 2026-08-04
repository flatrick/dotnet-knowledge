# Backlog

One file per known issue or deferred decision. Each states a current condition, why it
matters, the evidence for it, and a suggested fix — not a history of how it was found.

An item lives here when it is real, understood, and deliberately not fixed yet. Delete the
file when the item is resolved; `git log` is the record.

| Item | Area | Why it is deferred |
|---|---|---|
| [Git subprocesses hang under an MCP stdio host](git-subprocesses-hang-under-an-mcp-stdio-host.md) | server | Not deferred — blocks every tool that touches a synced source; the mechanism is still open |
| [Generic members are unreachable by name](generic-members-are-unreachable-by-name.md) | server | Needs a member-matching rule and a corrected `not_found` remedy |
| [lookup_api has no response budget](lookup-api-has-no-response-budget.md) | server | Needs a detail level and pagination on the lookup surface |
| [A non-generic type shadows its generic namesake](a-non-generic-type-shadows-its-generic-namesake.md) | server | Reachable via the backtick spelling `search_api` already returns |
| [Under-placement is unguarded](under-placement-is-unguarded.md) | corpus, tooling | Needs a new probe outcome; the tree is correct today |
| [The floor cache's scope key is unverified](floor-cache-scope-is-unverified.md) | tooling | Pre-existing; safe only by coincidence |
| [Probe detail strings are nondeterministic](probe-detail-is-nondeterministic.md) | tooling | Fix changes every compile in both languages |
| [Glob resolution is implemented twice](glob-resolution-is-implemented-twice.md) | tooling, tests | The two agree; sharing code across the boundary is not possible |
| [UnmanagedConstraintRecognition may need an exemption](unmanaged-constraint-recognition-exemption.md) | corpus | A corpus decision, not a tooling one |
| [The corpus build matrix dominates suite runtime](corpus-build-matrix-runtime.md) | tests | Tolerable today |
| [The probe omits RootNamespace](probe-omits-rootnamespace.md) | tooling | No current row is affected |
| [VbRows throws instead of exiting](vbrows-throws-instead-of-exiting.md) | tooling | Only reachable through a malformed layout |
| [C# has no measured-floor column](csharp-has-no-measured-floor-column.md) | corpus | Requires the C# tree to adopt per-pin placement first |
