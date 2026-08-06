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
| [`find_api_references` reads attribute applications in C# short form](attribute-references-use-csharp-short-form.md) | server | Resolving the suffix is decidable only inside an attribute application; the payload question is open |
| [`find_api_references` reports a parameterized base or interface as if it were the type](api-reference-kind-does-not-separate-exact-from-parameterized.md) | server | The payload already carries the answer in `typeExpression`; the fix is a convenience field |
| [`search_api` cannot ask for one namespace without its descendants](search-api-has-no-exact-namespace-query.md) | server | The descendant reading is the right default and no answer is wrong today |
| [No client has been observed rendering `sync_source`'s progress notifications](sync-source-progress-is-unverified.md) | server | The server emits them correctly; only the client's rendering is unknown, and it needs a person watching one |
| [Under-placement is unguarded](under-placement-is-unguarded.md) | corpus, tooling | Needs a new probe outcome; the tree is correct today |
| [The floor cache's scope key is unverified](floor-cache-scope-is-unverified.md) | tooling | Pre-existing; safe only by coincidence |
| [Probe detail strings are nondeterministic](probe-detail-is-nondeterministic.md) | tooling | Fix changes every compile in both languages |
| [Glob resolution is implemented twice](glob-resolution-is-implemented-twice.md) | tooling, tests | The two agree; sharing code across the boundary is not possible |
| [UnmanagedConstraintRecognition may need an exemption](unmanaged-constraint-recognition-exemption.md) | corpus | A corpus decision, not a tooling one |
| [The probe omits RootNamespace](probe-omits-rootnamespace.md) | tooling | No current row is affected |
| [VbRows throws instead of exiting](vbrows-throws-instead-of-exiting.md) | tooling | Only reachable through a malformed layout |
| [C# has no measured-floor column](csharp-has-no-measured-floor-column.md) | corpus | Requires the C# tree to adopt per-pin placement first |
