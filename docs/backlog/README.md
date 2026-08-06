# Backlog

One file per known issue or deferred decision. Each states a current condition, why it
matters, the evidence for it, and a suggested fix — not a history of how it was found.

An item lives here when it is real, understood, and deliberately not fixed yet. Delete the
file when the item is resolved; `git log` is the record.

| Item | Area | Why it is deferred |
|---|---|---|
| [The MCP Tasks extension is not adopted](mcp-tasks-extension-is-not-adopted.md) | server | Client-negotiated and unsupported by the target client; progress notifications cover the need |
| [`lookup_api`'s detail level is decided across sources](lookup-api-detail-level-is-decided-across-sources.md) | server | Unreachable with the two configured sources, whose namespace trees are disjoint |
| [`lookup_api` reports `not_found` at the last page boundary](lookup-api-reports-not-found-at-the-last-page-boundary.md) | server | Unreachable without a hand-constructed cursor; `search_api` is already correct here |
| [`find_api_references` omits generic constraints and attribute usages](find-api-references-omits-constraints-and-attributes.md) | server | Adding them wants new `kind` values, which is a payload decision |
| [`find_api_references` reports a parameterized base or interface as if it were the type](api-reference-kind-does-not-separate-exact-from-parameterized.md) | server | The payload already carries the answer in `typeExpression`; the fix is a convenience field |
| [`search_api` cannot ask for one namespace without its descendants](search-api-has-no-exact-namespace-query.md) | server | The descendant reading is the right default and no answer is wrong today |
| [`sync_source`'s progress notifications have never been observed](sync-source-progress-is-unverified.md) | server | The sync itself works; the notification is advisory |
| [Glob resolution is implemented twice](glob-resolution-is-implemented-twice.md) | tooling, tests | The two agree; sharing code across the boundary is not possible |
| [The corpus build matrix dominates suite runtime](corpus-build-matrix-runtime.md) | tests | Tolerable today |
| [C# has no measured-floor column](csharp-has-no-measured-floor-column.md) | corpus | Requires the C# tree to adopt per-pin placement first |
