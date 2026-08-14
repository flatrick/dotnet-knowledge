# Backlog

One file per known issue or deferred decision. Each states a current condition, why it
matters, the evidence for it, and a suggested fix — not a history of how it was found.

An item lives here when it is real, understood, and deliberately not fixed yet. Delete the
file when the item is resolved; `git log` is the record.

| Item | Area | Why it is deferred |
|---|---|---|
| [The MCP Tasks extension is not adopted](mcp-tasks-extension-is-not-adopted.md) | server | Client-negotiated and unsupported by the target client; progress notifications cover the need |
| [No client has been observed rendering `sync_source`'s progress notifications](sync-source-progress-is-unverified.md) | server | The server emits them correctly; only the client's rendering is unknown, and it needs a person watching one |
| [YAML content in a synchronized source is unsearchable](yaml-source-content-is-unsearchable.md) | server | Three pipeline stages assume markdown; the cheapest honest fix is narrower than full support and not yet designed |
| [Unfiltered document search has no relevance signal](cross-source-search-has-no-relevance-signal.md) | server | The `nuget-docs` tiering works around the worst case; scoring against the query is real design work not yet done |
| [Every document search rescans every file in every source](document-search-rescans-every-file.md) | server | Unmeasured whether the cost is a real problem; an index would need sync-time invalidation |
| [Framework selection has no observable effect on any measured package](framework-selection-has-no-observable-effect.md) | sources | Every Roslyn package measured has one surface across all its frameworks; removal is wide and every sample so far is a Roslyn one |
| [One undecodable member fails the whole package](undecodable-metadata-fails-the-whole-package.md) | sources | 19% of first-party packages still refuse; degrading instead needs a reported-skip field, and reporting it honestly is a corpus schema change |
| [Every API query revalidates the checkout with git](every-api-query-revalidates-the-checkout.md) | server | The validation is what makes an answer trustworthy; which of the three commands costs the second is unmeasured |
